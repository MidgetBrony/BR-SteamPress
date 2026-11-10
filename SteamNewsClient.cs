using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace BR_SteamPress
{
    internal sealed class SteamNewsClient : IDisposable
    {
        private static readonly Regex ImageBbCode = new(@"\[(?:img|img=\d+x\d+)\](?<url>https?://[^\[]+)\[/img\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlImage = new("<img[^>]+src=[\"'](?<url>https?://[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UrlTag = new(@"\[url(?:=[^\]]+)?\](?<text>.*?)\[/url\]", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex BbCode = new(@"\[/?[a-z0-9_*]+(?:=[^\]]*)?\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Html = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ExcessWhitespace = new(@"[ \t]+", RegexOptions.Compiled);
        private static readonly Regex ExcessLines = new(@"\n{3,}", RegexOptions.Compiled);
        private static readonly Regex CamelCaseBoundary = new(@"(?<=[a-z0-9.!?)])(?=[A-Z][a-z])", RegexOptions.Compiled);

        private readonly HttpClient http;
        private readonly SemaphoreSlim limiter = new(4, 4);

        internal SteamNewsClient()
        {
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BR-SteamPress/0.1 (+BOXROOM mod)");
        }

        internal async Task<IReadOnlyList<NewsStory>> FetchAsync(IEnumerable<SteamGame> games, int lookbackDays, int maximumStories)
        {
            SteamGame[] selected = games.ToArray();
            Task<List<NewsStory>>[] requests = selected.Select(game => FetchGameAsync(game, lookbackDays)).ToArray();
            List<NewsStory>[] batches = await Task.WhenAll(requests).ConfigureAwait(false);
            NewsStory[] stories = batches.SelectMany(batch => batch)
                .GroupBy(story => $"{story.AppId}|{story.Title}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(story => story.PublishedUnix).First())
                .GroupBy(story => story.AppId)
                .SelectMany(group => group.OrderByDescending(story => story.PublishedUnix).Take(2))
                .OrderByDescending(story => story.PublishedUnix)
                .Take(maximumStories)
                .ToArray();

            await Task.WhenAll(stories.Select(DownloadLeadImageAsync)).ConfigureAwait(false);
            return stories;
        }

        private async Task<List<NewsStory>> FetchGameAsync(SteamGame game, int lookbackDays)
        {
            await limiter.WaitAsync().ConfigureAwait(false);
            try
            {
                long endDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid={game.AppId}&count=4&maxlength=5000&enddate={endDate}&format=json&l=english";
                string json = await http.GetStringAsync(url).ConfigureAwait(false);
                SteamNewsEnvelope envelope = JsonConvert.DeserializeObject<SteamNewsEnvelope>(json);
                long cutoff = DateTimeOffset.UtcNow.AddDays(-lookbackDays).ToUnixTimeSeconds();
                return (envelope?.AppNews?.NewsItems ?? new List<SteamNewsItem>())
                    .Where(item => item.Date >= cutoff && !string.IsNullOrWhiteSpace(item.Title) && IsPredominantlyLatin(item.Title))
                    .Select(item => ToStory(game, item))
                    .Where(story => !string.IsNullOrWhiteSpace(story.Body))
                    .ToList();
            }
            catch
            {
                return new List<NewsStory>();
            }
            finally
            {
                limiter.Release();
            }
        }

        private static NewsStory ToStory(SteamGame game, SteamNewsItem item)
        {
            string raw = item.Contents ?? string.Empty;
            Match image = ImageBbCode.Match(raw);
            if (!image.Success) image = HtmlImage.Match(raw);
            return new NewsStory
            {
                AppId = game.AppId,
                GameName = game.Name,
                Gid = item.Gid,
                Title = CleanInline(item.Title),
                Author = CleanInline(item.Author),
                FeedLabel = CleanInline(item.FeedLabel),
                Url = item.Url,
                PublishedUnix = item.Date,
                Body = CleanBody(raw),
                ImageUrl = image.Success ? WebUtility.HtmlDecode(image.Groups["url"].Value.Trim()) : null
            };
        }

        private async Task DownloadLeadImageAsync(NewsStory story)
        {
            string[] candidates = string.IsNullOrWhiteSpace(story.ImageUrl)
                ? new[] { $"https://cdn.cloudflare.steamstatic.com/steam/apps/{story.AppId}/header.jpg" }
                : new[] { story.ImageUrl, $"https://cdn.cloudflare.steamstatic.com/steam/apps/{story.AppId}/header.jpg" };

            foreach (string candidate in candidates)
            {
                if (!Uri.TryCreate(candidate, UriKind.Absolute, out Uri uri)) continue;
                if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
                try
                {
                    using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode) continue;
                    long? length = response.Content.Headers.ContentLength;
                    if (length.HasValue && length.Value > 8 * 1024 * 1024) continue;
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                    if (bytes.Length > 8 * 1024 * 1024) continue;
                    story.ImageBytes = bytes;
                    return;
                }
                catch { }
            }
        }

        private static string CleanBody(string value)
        {
            string text = value ?? string.Empty;
            text = ImageBbCode.Replace(text, "\n");
            text = HtmlImage.Replace(text, "\n");
            text = UrlTag.Replace(text, match => match.Groups["text"].Value);
            text = Regex.Replace(text, @"\[list\]|\[/list\]", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\[\*\]", "\n• ", RegexOptions.IgnoreCase);
            text = BbCode.Replace(text, string.Empty);
            text = Regex.Replace(text, "<(?:br|/p|/div|/li|/h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
            text = Html.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text).Replace("\r", string.Empty);
            text = Regex.Replace(text, @"\\(?=[A-Z])", "\n• ");
            if (CamelCaseBoundary.Matches(text).Count >= 3)
                text = CamelCaseBoundary.Replace(text, "\n• ");
            text = ExcessWhitespace.Replace(text, " ");
            return ExcessLines.Replace(text, "\n\n").Trim();
        }

        private static bool IsPredominantlyLatin(string value)
        {
            int letters = 0;
            int latin = 0;
            foreach (char character in value ?? string.Empty)
            {
                if (!char.IsLetter(character)) continue;
                letters++;
                if (character <= '\u024f') latin++;
            }
            return letters == 0 || latin / (double)letters >= 0.8;
        }

        private static string CleanInline(string value) =>
            ExcessWhitespace.Replace(WebUtility.HtmlDecode(Html.Replace(BbCode.Replace(value ?? string.Empty, string.Empty), string.Empty)), " ").Trim();

        public void Dispose()
        {
            limiter.Dispose();
            http.Dispose();
        }
    }
}
