using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace BR_SteamPress
{
    internal sealed class RssFeedClient : IDisposable
    {
        private static readonly Regex HtmlImage = new("<img[^>]+src=[\"'](?<url>https?://[^\"']+)[\"']", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlBreak = new("<(?:br|/p|/div|/li|/h[1-6])[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlTag = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex ExcessWhitespace = new(@"[ \t]+", RegexOptions.Compiled);
        private static readonly Regex ExcessLines = new(@"\n{3,}", RegexOptions.Compiled);

        private readonly HttpClient http;
        private readonly SemaphoreSlim limiter = new(4, 4);

        internal RssFeedClient()
        {
            http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("BR-SteamPress/0.3.3 (+BOXROOM RSS reader)");
        }

        internal async Task<IReadOnlyList<NewsStory>> FetchAsync(IEnumerable<string> feedUrls, int lookbackDays, int maximumStories)
        {
            Task<List<NewsStory>>[] requests = feedUrls.Select(url => FetchFeedAsync(url, lookbackDays)).ToArray();
            List<NewsStory>[] batches = await Task.WhenAll(requests).ConfigureAwait(false);
            NewsStory[] stories = batches.SelectMany(batch => batch)
                .GroupBy(story => string.IsNullOrWhiteSpace(story.Gid) ? story.Url : story.Gid, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(story => story.PublishedUnix).First())
                .GroupBy(story => story.GameName, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group.OrderByDescending(story => story.PublishedUnix).Take(4))
                .OrderByDescending(story => story.PublishedUnix)
                .Take(maximumStories)
                .ToArray();

            await Task.WhenAll(stories.Select(DownloadImageAsync)).ConfigureAwait(false);
            return stories;
        }

        private async Task<List<NewsStory>> FetchFeedAsync(string url, int lookbackDays)
        {
            await limiter.WaitAsync().ConfigureAwait(false);
            try
            {
                using HttpResponseMessage response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length > 4 * 1024 * 1024)
                    return new List<NewsStory>();

                using Stream stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4 * 1024 * 1024,
                    IgnoreComments = true
                };
                using XmlReader reader = XmlReader.Create(stream, settings);
                XDocument document = XDocument.Load(reader, LoadOptions.None);
                return Parse(document, response.RequestMessage?.RequestUri ?? new Uri(url), lookbackDays);
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

        private static List<NewsStory> Parse(XDocument document, Uri feedUri, int lookbackDays)
        {
            XElement root = document.Root;
            if (root == null) return new List<NewsStory>();
            bool atom = string.Equals(root.Name.LocalName, "feed", StringComparison.OrdinalIgnoreCase);
            XElement container = atom ? root : root.Descendants().FirstOrDefault(element => element.Name.LocalName == "channel") ?? root;
            string feedTitle = ChildValue(container, "title");
            if (string.IsNullOrWhiteSpace(feedTitle)) feedTitle = feedUri.Host;
            string feedImage = FindFeedImage(container, feedUri);
            string entryName = atom ? "entry" : "item";
            long cutoff = DateTimeOffset.UtcNow.AddDays(-lookbackDays).ToUnixTimeSeconds();
            var result = new List<NewsStory>();

            foreach (XElement entry in container.Descendants().Where(element => element.Name.LocalName == entryName).Take(50))
            {
                string title = CleanInline(ChildValue(entry, "title"));
                if (string.IsNullOrWhiteSpace(title)) continue;
                string bodyRaw = FirstChildValue(entry, "encoded", "content", "description", "summary");
                string body = CleanBody(bodyRaw);
                if (string.IsNullOrWhiteSpace(body)) continue;
                long published = ParseDate(FirstChildValue(entry, "published", "updated", "pubDate", "date"));
                if (published == 0) published = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (published < cutoff) continue;

                string link = atom ? AtomLink(entry) : ChildValue(entry, "link");
                if (Uri.TryCreate(feedUri, link, out Uri absoluteLink)) link = absoluteLink.AbsoluteUri;
                string gid = FirstChildValue(entry, "id", "guid");
                string image = FindEntryImage(entry, bodyRaw, feedUri) ?? feedImage;
                result.Add(new NewsStory
                {
                    AppId = 0,
                    GameName = feedTitle,
                    Gid = string.IsNullOrWhiteSpace(gid) ? link : gid,
                    Title = title,
                    Author = CleanInline(FirstChildValue(entry, "creator", "author")),
                    FeedLabel = feedTitle,
                    Url = link,
                    PublishedUnix = published,
                    Body = body,
                    ImageUrl = image
                });
            }
            return result;
        }

        private async Task DownloadImageAsync(NewsStory story)
        {
            if (!Uri.TryCreate(story.ImageUrl, UriKind.Absolute, out Uri uri)) return;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
            try
            {
                using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return;
                if (response.Content.Headers.ContentLength is long length && length > 8 * 1024 * 1024) return;
                byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length <= 8 * 1024 * 1024) story.ImageBytes = bytes;
            }
            catch { }
        }

        private static string FindFeedImage(XElement container, Uri baseUri)
        {
            XElement image = container.Descendants().FirstOrDefault(element => element.Name.LocalName == "image");
            string value = image == null ? null : FirstChildValue(image, "url", "href");
            return ResolveUrl(baseUri, value);
        }

        private static string FindEntryImage(XElement entry, string html, Uri baseUri)
        {
            XElement media = entry.Descendants().FirstOrDefault(element =>
                (element.Name.LocalName == "content" || element.Name.LocalName == "thumbnail" || element.Name.LocalName == "enclosure") &&
                (element.Attribute("url") != null || element.Attribute("href") != null) &&
                ((string)element.Attribute("type") ?? (string)element.Attribute("medium") ?? "image").StartsWith("image", StringComparison.OrdinalIgnoreCase));
            string value = (string)media?.Attribute("url") ?? (string)media?.Attribute("href");
            if (string.IsNullOrWhiteSpace(value))
            {
                Match match = HtmlImage.Match(html ?? string.Empty);
                if (match.Success) value = WebUtility.HtmlDecode(match.Groups["url"].Value);
            }
            return ResolveUrl(baseUri, value);
        }

        private static string ResolveUrl(Uri baseUri, string value) =>
            Uri.TryCreate(baseUri, value, out Uri result) && (result.Scheme == Uri.UriSchemeHttp || result.Scheme == Uri.UriSchemeHttps)
                ? result.AbsoluteUri
                : null;

        private static string AtomLink(XElement entry)
        {
            XElement link = entry.Elements().FirstOrDefault(element => element.Name.LocalName == "link" &&
                ((string)element.Attribute("rel") == "alternate" || element.Attribute("rel") == null));
            return (string)link?.Attribute("href") ?? link?.Value;
        }

        private static string ChildValue(XElement parent, string localName) =>
            parent?.Elements().FirstOrDefault(element => element.Name.LocalName == localName)?.Value;

        private static string FirstChildValue(XElement parent, params string[] localNames)
        {
            foreach (string name in localNames)
            {
                string value = ChildValue(parent, name);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static long ParseDate(string value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out DateTimeOffset parsed)
                ? parsed.ToUnixTimeSeconds()
                : 0;

        private static string CleanBody(string value)
        {
            string text = HtmlImage.Replace(value ?? string.Empty, "\n");
            text = HtmlBreak.Replace(text, "\n");
            text = HtmlTag.Replace(text, string.Empty);
            text = WebUtility.HtmlDecode(text).Replace("\r", string.Empty);
            text = ExcessWhitespace.Replace(text, " ");
            return ExcessLines.Replace(text, "\n\n").Trim();
        }

        private static string CleanInline(string value) =>
            ExcessWhitespace.Replace(WebUtility.HtmlDecode(HtmlTag.Replace(value ?? string.Empty, string.Empty)), " ").Trim();

        public void Dispose()
        {
            limiter.Dispose();
            http.Dispose();
        }
    }
}
