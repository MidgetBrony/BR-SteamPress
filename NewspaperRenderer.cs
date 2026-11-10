using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace BR_SteamPress
{
    internal sealed class RenderedIssue
    {
        internal byte[] CoverJpeg { get; set; }
        internal IReadOnlyList<byte[]> Pages { get; set; }
    }

    internal static class NewspaperRenderer
    {
        private const int Width = 1600;
        private const int Height = 2200;
        private const float Margin = 105f;
        private static readonly SKColor Paper = new(246, 240, 222);
        private static readonly SKColor Ink = new(28, 27, 24);
        private static readonly SKColor Accent = new(26, 78, 112);
        private const string ParagraphBreak = "\u241e";

        internal static RenderedIssue Render(DateTime issueDate, IReadOnlyList<NewsStory> stories)
        {
            byte[] cover = RenderCover(issueDate, stories);
            var pages = new List<byte[]> { cover };
            int nextPage = 2;
            foreach (NewsStory story in stories)
            {
                IReadOnlyList<byte[]> storyPages = RenderStory(story, nextPage);
                pages.AddRange(storyPages);
                nextPage += storyPages.Count;
            }
            return new RenderedIssue { CoverJpeg = cover, Pages = pages };
        }

        internal static void WriteCbz(string path, IReadOnlyList<byte[]> pages)
        {
            using FileStream stream = File.Create(path);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            for (int i = 0; i < pages.Count; i++)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"{i + 1:000}.jpg", CompressionLevel.Optimal);
                using Stream output = entry.Open();
                output.Write(pages[i], 0, pages[i].Length);
            }
        }

        private static byte[] RenderCover(DateTime issueDate, IReadOnlyList<NewsStory> stories)
        {
            return MagazineCoverRenderer.Render(issueDate, stories);
        }

        private static IReadOnlyList<byte[]> RenderStory(NewsStory story, int firstPageNumber)
        {
            var result = new List<byte[]>();
            var words = SplitBodyWords(story.Body);
            int wordIndex = 0;
            bool first = true;
            do
            {
                using SKBitmap bitmap = NewPage();
                using SKCanvas canvas = new(bitmap);
                float y = 95;
                DrawText(canvas, "THE BOXROOM PRESS", Margin, y, 27, Accent, SKFontStyle.Bold);
                DrawTextRight(canvas, DateTimeOffset.FromUnixTimeSeconds(story.PublishedUnix).LocalDateTime.ToString("d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant(), Width - Margin, y, 24, Ink);
                y += 54;
                using (SKPaint topRule = GraphicPaint(Ink, 4)) canvas.DrawLine(Margin, y, Width - Margin, y, topRule);
                y += 70;

                DrawText(canvas, story.GameName.ToUpperInvariant(), Margin, y, 30, Accent, SKFontStyle.Bold);
                y += 55;
                y = DrawWrapped(canvas, first ? story.Title : story.Title + " — CONTINUED", Margin, y, Width - Margin * 2, first ? 62 : 48, 1.08f, Ink, SKFontStyle.Bold, first ? 4 : 3);
                y += 25;
                string byline = BuildByline(story);
                DrawText(canvas, byline, Margin, y, 23, new SKColor(80, 76, 68), SKFontStyle.Italic);
                y += 45;

                if (first && story.ImageBytes != null && TryDecode(story.ImageBytes, out SKBitmap image))
                {
                    float imageHeight = 520;
                    using (image) DrawFit(canvas, image, new SKRect(Margin, y, Width - Margin, y + imageHeight));
                    y += imageHeight + 45;
                }

                float bottom = Height - 135;
                int nextIndex = DrawBodyColumns(canvas, words, wordIndex, y, bottom);
                if (nextIndex <= wordIndex && wordIndex < words.Count) nextIndex++;
                wordIndex = nextIndex;
                DrawFooter(canvas, story.Url ?? "STEAM COMMUNITY NEWS", firstPageNumber + result.Count);
                result.Add(Encode(bitmap, 89));
                first = false;
            } while (wordIndex < words.Count && result.Count < 6);
            return result;
        }

        private static int DrawBodyColumns(SKCanvas canvas, IReadOnlyList<string> words, int start, float top, float bottom)
        {
            const float gap = 58;
            float columnWidth = (Width - Margin * 2 - gap) / 2;
            using SKTypeface typeface = SKTypeface.FromFamilyName("Georgia", SKFontStyle.Normal);
            using SKFont bodyFont = new(typeface, 31);
            using SKPaint bodyPaint = GraphicPaint(Ink);
            float lineHeight = 42;
            int index = start;
            for (int column = 0; column < 2 && index < words.Count; column++)
            {
                float x = Margin + column * (columnWidth + gap);
                float y = top;
                while (index < words.Count && y + lineHeight <= bottom)
                {
                    string line = NextLine(words, ref index, bodyFont, columnWidth);
                    canvas.DrawText(line, x, y, SKTextAlign.Left, bodyFont, bodyPaint);
                    y += lineHeight;
                }
            }
            return index;
        }

        private static string NextLine(IReadOnlyList<string> words, ref int index, SKFont font, float width)
        {
            string line = string.Empty;
            while (index < words.Count)
            {
                if (words[index] == ParagraphBreak)
                {
                    index++;
                    if (line.Length == 0) return string.Empty;
                    break;
                }
                string candidate = line.Length == 0 ? words[index] : line + " " + words[index];
                if (line.Length > 0 && font.MeasureText(candidate) > width) break;
                line = candidate;
                index++;
            }
            return line;
        }

        private static List<string> SplitWords(string text) =>
            (text ?? string.Empty).Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).ToList();

        private static List<string> SplitBodyWords(string text) =>
            (text ?? string.Empty)
                .Replace("\r", string.Empty)
                .Replace("\n", $" {ParagraphBreak} ")
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .ToList();

        private static void DrawMasthead(SKCanvas canvas, DateTime date, string edition)
        {
            DrawCentered(canvas, "THE BOXROOM PRESS", 155, 88, SKFontStyle.Bold);
            using SKPaint rule = GraphicPaint(Ink, 6);
            canvas.DrawLine(Margin, 215, Width - Margin, 215, rule);
            DrawText(canvas, date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture).ToUpperInvariant(), Margin, 267, 25, Ink, SKFontStyle.Bold);
            DrawTextRight(canvas, edition, Width - Margin, 267, 25, Accent, SKFontStyle.Bold);
            using SKPaint lowerRule = GraphicPaint(Ink, 2);
            canvas.DrawLine(Margin, 295, Width - Margin, 295, lowerRule);
        }

        private static string BuildByline(NewsStory story)
        {
            string author = string.IsNullOrWhiteSpace(story.Author) ? story.FeedLabel : story.Author;
            string date = DateTimeOffset.FromUnixTimeSeconds(story.PublishedUnix).LocalDateTime.ToString("d MMM yyyy", CultureInfo.CurrentCulture);
            return string.IsNullOrWhiteSpace(author) ? date : $"{author}  •  {date}";
        }

        private static void DrawFooter(SKCanvas canvas, string text, int page)
        {
            using SKPaint rule = GraphicPaint(new SKColor(105, 99, 86), 2);
            canvas.DrawLine(Margin, Height - 92, Width - Margin, Height - 92, rule);
            string safe = string.IsNullOrWhiteSpace(text) ? "THE BOXROOM PRESS" : text;
            if (safe.Length > 105) safe = safe.Substring(0, 102) + "...";
            DrawText(canvas, safe, Margin, Height - 48, 18, new SKColor(90, 85, 75));
            DrawTextRight(canvas, page.ToString(CultureInfo.InvariantCulture), Width - Margin, Height - 48, 18, new SKColor(90, 85, 75));
        }

        private static float DrawWrapped(SKCanvas canvas, string text, float x, float y, float width, float size, float lineSpacing, SKColor color, SKFontStyle style, int maxLines)
        {
            using SKTypeface typeface = SKTypeface.FromFamilyName("Georgia", style);
            using SKFont font = new(typeface, size);
            using SKPaint paint = GraphicPaint(color);
            var words = SplitWords(text);
            int index = 0;
            int lines = 0;
            while (index < words.Count && lines < maxLines)
            {
                string line = NextLine(words, ref index, font, width);
                if (lines == maxLines - 1 && index < words.Count) line = line.TrimEnd('.', '…') + "…";
                canvas.DrawText(line, x, y, SKTextAlign.Left, font, paint);
                y += size * lineSpacing;
                lines++;
            }
            return y;
        }

        private static void DrawCentered(SKCanvas canvas, string text, float y, float size, SKFontStyle style, SKColor? color = null)
        {
            using SKTypeface typeface = SKTypeface.FromFamilyName("Georgia", style);
            using SKFont font = new(typeface, size);
            using SKPaint paint = GraphicPaint(color ?? Ink);
            canvas.DrawText(text, (Width - font.MeasureText(text)) / 2f, y, SKTextAlign.Left, font, paint);
        }

        private static void DrawText(SKCanvas canvas, string text, float x, float y, float size, SKColor color, SKFontStyle style = null)
        {
            using SKTypeface typeface = SKTypeface.FromFamilyName("Georgia", style ?? SKFontStyle.Normal);
            using SKFont font = new(typeface, size);
            using SKPaint paint = GraphicPaint(color);
            canvas.DrawText(text ?? string.Empty, x, y, SKTextAlign.Left, font, paint);
        }

        private static void DrawTextRight(SKCanvas canvas, string text, float right, float y, float size, SKColor color, SKFontStyle style = null)
        {
            using SKTypeface typeface = SKTypeface.FromFamilyName("Georgia", style ?? SKFontStyle.Normal);
            using SKFont font = new(typeface, size);
            using SKPaint paint = GraphicPaint(color);
            canvas.DrawText(text ?? string.Empty, right - font.MeasureText(text ?? string.Empty), y, SKTextAlign.Left, font, paint);
        }

        private static SKPaint GraphicPaint(SKColor color, float strokeWidth = 1) => new()
        {
            Color = color,
            IsAntialias = true,
            StrokeWidth = strokeWidth
        };

        private static SKBitmap NewPage()
        {
            var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using SKCanvas canvas = new(bitmap);
            canvas.Clear(Paper);
            return bitmap;
        }

        private static bool TryDecode(byte[] bytes, out SKBitmap bitmap)
        {
            try { bitmap = SKBitmap.Decode(bytes); return bitmap != null && bitmap.Width > 0 && bitmap.Height > 0; }
            catch { bitmap = null; return false; }
        }

        private static void DrawCrop(SKCanvas canvas, SKBitmap image, SKRect destination)
        {
            float sourceRatio = image.Width / (float)image.Height;
            float targetRatio = destination.Width / destination.Height;
            SKRect source;
            if (sourceRatio > targetRatio)
            {
                float width = image.Height * targetRatio;
                float left = (image.Width - width) / 2f;
                source = new SKRect(left, 0, left + width, image.Height);
            }
            else
            {
                float height = image.Width / targetRatio;
                float top = (image.Height - height) / 2f;
                source = new SKRect(0, top, image.Width, top + height);
            }
            canvas.DrawBitmap(image, source, destination);
        }

        private static void DrawFit(SKCanvas canvas, SKBitmap image, SKRect destination)
        {
            float scale = Math.Min(destination.Width / image.Width, destination.Height / image.Height);
            float width = image.Width * scale;
            float height = image.Height * scale;
            float left = destination.MidX - width / 2f;
            float top = destination.MidY - height / 2f;
            canvas.DrawBitmap(image, new SKRect(left, top, left + width, top + height));
        }

        private static byte[] Encode(SKBitmap bitmap, int quality)
        {
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            return data.ToArray();
        }
    }
}
