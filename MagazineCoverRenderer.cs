using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BR_SteamPress
{
    internal static class MagazineCoverRenderer
    {
        private const int Width = 1600;
        private const int Height = 2200;
        private static readonly SKColor Burgundy = new(71, 46, 54);
        private static readonly SKColor Gold = new(229, 188, 9);
        private static readonly SKColor OffWhite = new(246, 246, 246);
        private static readonly SKColor Black = new(8, 8, 8);
        private static readonly Lazy<SKTypeface> DisplayTypeface = new(() => LoadTypeface("LilitaOne-Regular.ttf", "Arial Black"));
        private static readonly Lazy<SKTypeface> InterfaceTypeface = new(() => LoadTypeface("YanoneKaffeesatz.ttf", "Arial Narrow"));

        private sealed class CoverLayout
        {
            internal CoverLayout(SKRect mascot, SKRect lead, SKRect supporting, bool barcodeRight)
            {
                Mascot = mascot;
                Lead = lead;
                Supporting = supporting;
                BarcodeRight = barcodeRight;
            }

            internal SKRect Mascot { get; }
            internal SKRect Lead { get; }
            internal SKRect Supporting { get; }
            internal bool BarcodeRight { get; }
        }

        private static readonly CoverLayout[] Layouts =
        {
            new(new SKRect(360, 300, 1565, 2110), new SKRect(80, 590, 650, 1090), new SKRect(80, 1160, 650, 1720), false),
            new(new SKRect(340, 380, 1300, 2110), new SKRect(75, 600, 590, 1080), new SKRect(1050, 1000, 1520, 1660), false),
            new(new SKRect(580, 360, 1530, 2110), new SKRect(75, 600, 635, 1090), new SKRect(80, 1160, 625, 1740), true),
            new(new SKRect(65, 350, 1160, 2070), new SKRect(970, 575, 1520, 1080), new SKRect(1015, 1140, 1520, 1710), false),
            new(new SKRect(500, 365, 1535, 2090), new SKRect(70, 590, 625, 1100), new SKRect(80, 1160, 620, 1730), true),
            new(new SKRect(570, 360, 1535, 2100), new SKRect(70, 610, 625, 1100), new SKRect(80, 1180, 620, 1740), true)
        };

        internal static byte[] Render(DateTime issueDate, IReadOnlyList<NewsStory> stories)
        {
            int seed = unchecked(issueDate.Year * 397 + issueDate.DayOfYear * 17);
            int mascotIndex = Math.Abs(seed % Layouts.Length);
            CoverLayout layout = Layouts[mascotIndex];

            using var bitmap = new SKBitmap(Width, Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var canvas = new SKCanvas(bitmap);
            DrawBackground(canvas);
            DrawMasthead(canvas, issueDate);
            DrawMascot(canvas, mascotIndex + 1, layout.Mascot);

            if (stories == null || stories.Count == 0)
            {
                DrawLead(canvas, "THE BOXROOM PRESS", "NO NEWS IS GOOD NEWS", layout.Lead);
            }
            else
            {
                DrawLead(canvas, stories[0].GameName, stories[0].Title, layout.Lead);
                DrawSupporting(canvas, stories.Skip(1).Take(3).ToArray(), layout.Supporting);
            }

            DrawBarcode(canvas, issueDate, layout.BarcodeRight, seed);
            DrawBottomLine(canvas, issueDate, stories?.Count ?? 0);

            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Jpeg, 93);
            return data.ToArray();
        }

        private static void DrawBackground(SKCanvas canvas)
        {
            canvas.Clear(Burgundy);
            using var minor = Paint(new SKColor(Gold.Red, Gold.Green, Gold.Blue, 28), 1);
            using var major = Paint(new SKColor(Gold.Red, Gold.Green, Gold.Blue, 66), 2);
            for (int x = 0; x <= Width; x += 32) canvas.DrawLine(x, 0, x, Height, x % 160 == 0 ? major : minor);
            for (int y = 0; y <= Height; y += 32) canvas.DrawLine(0, y, Width, y, y % 160 == 0 ? major : minor);

            using var border = Paint(Gold, 5, SKPaintStyle.Stroke);
            canvas.DrawRect(new SKRect(24, 24, Width - 24, Height - 24), border);
        }

        private static void DrawMasthead(SKCanvas canvas, DateTime issueDate)
        {
            string issue = $"ISSUE {issueDate.DayOfYear:000}  •  {issueDate:dd MMM yyyy}".ToUpperInvariant();
            DrawInterface(canvas, "STEAM NEWS • UPDATES • RELEASES", 72, 62, 28, OffWhite);
            DrawInterfaceRight(canvas, issue, Width - 72, 62, 28, Gold);

            using (SKBitmap logo = LoadBitmap("BoxroomLogo.png"))
            {
                if (logo != null) DrawFit(canvas, logo, new SKRect(64, 82, 1055, 320));
            }

            DrawOutlinedDisplay(canvas, "PRESS", 1090, 273, 145, OffWhite, Black, Gold);
            using var rule = Paint(Gold, 6);
            canvas.DrawLine(66, 338, Width - 66, 338, rule);
            DrawInterface(canvas, "THE DAILY BOXROOM MAGAZINE", 70, 378, 31, OffWhite);
            DrawInterfaceRight(canvas, "FREE EDITION", Width - 70, 378, 31, Gold);
        }

        private static void DrawMascot(SKCanvas canvas, int number, SKRect destination)
        {
            using SKBitmap mascot = LoadBitmap($"Boxroom_Mascot_{number}.png");
            if (mascot == null) return;
            SKRect source = number switch
            {
                1 => new SKRect(210, 0, mascot.Width, mascot.Height),
                6 => new SKRect(502, 272, 2998, 4950),
                _ => new SKRect(0, 0, mascot.Width, mascot.Height)
            };
            using var shadow = Paint(new SKColor(0, 0, 0, 80));
            DrawFit(canvas, mascot, source, Offset(destination, 15, 18), shadow);
            DrawFit(canvas, mascot, source, destination, null);
        }

        private static void DrawLead(SKCanvas canvas, string label, string title, SKRect area)
        {
            using var panel = Paint(new SKColor(Burgundy.Red, Burgundy.Green, Burgundy.Blue, 230));
            canvas.DrawRect(area, panel);
            using var edge = Paint(Gold, 7);
            canvas.DrawLine(area.Left, area.Top, area.Right, area.Top, edge);

            float y = area.Top + 48;
            DrawInterface(canvas, SafeUpper(label, "TODAY'S LEAD"), area.Left + 18, y, 31, Gold);
            y += 30;
            DrawDisplayWrapped(canvas, SafeUpper(title, "NO NEWS IS GOOD NEWS"), area.Left + 18, y + 78,
                area.Width - 36, 108, 0.94f, 4, OffWhite, true);
        }

        private static void DrawSupporting(SKCanvas canvas, IReadOnlyList<NewsStory> stories, SKRect area)
        {
            if (stories == null || stories.Count == 0) return;
            float y = area.Top;
            float itemHeight = Math.Min(190, area.Height / stories.Count);
            foreach (NewsStory story in stories)
            {
                using var panel = Paint(new SKColor(Burgundy.Red, Burgundy.Green, Burgundy.Blue, 218));
                canvas.DrawRect(new SKRect(area.Left, y, area.Right, y + itemHeight - 22), panel);
                using var rule = Paint(Gold, 4);
                canvas.DrawLine(area.Left, y, area.Left + 110, y, rule);
                DrawInterface(canvas, SafeUpper(story.GameName, "NEWS"), area.Left + 12, y + 34, 25, Gold);
                DrawInterfaceWrapped(canvas, story.Title, area.Left + 12, y + 78, area.Width - 24, 43, 1.02f, 2, OffWhite);
                y += itemHeight;
            }
        }

        private static void DrawBarcode(SKCanvas canvas, DateTime issueDate, bool right, int seed)
        {
            float left = right ? Width - 365 : 64;
            var panel = new SKRect(left, Height - 310, left + 300, Height - 92);
            using var paper = Paint(OffWhite);
            canvas.DrawRect(panel, paper);
            using var frame = Paint(Gold, 12, SKPaintStyle.Stroke);
            canvas.DrawRect(panel, frame);

            DrawInterfaceCentered(canvas, "BOXROOM: FREE", panel.MidX, panel.Top + 40, 27, Black);
            DrawInterfaceCentered(canvas, "DAILY EDITION", panel.MidX, panel.Top + 70, 23, Black);

            var random = new Random(seed);
            float x = panel.Left + 43;
            float bottom = panel.Bottom - 38;
            using var bars = Paint(Black);
            while (x < panel.Right - 43)
            {
                float width = random.Next(2, 7);
                float gap = random.Next(3, 8);
                float height = random.Next(48, 82);
                canvas.DrawRect(x, bottom - height, width, height, bars);
                x += width + gap;
            }

            string code = $"{issueDate:yyyy} {issueDate.DayOfYear:000} {issueDate:dd}";
            DrawInterfaceCentered(canvas, code, panel.MidX, panel.Bottom - 12, 19, Black);
        }

        private static void DrawBottomLine(SKCanvas canvas, DateTime issueDate, int storyCount)
        {
            string text = $"THE BOXROOM PRESS  •  {storyCount} STORIES  •  VOL. {issueDate:yyyy}  •  NO. {issueDate.DayOfYear:000}";
            DrawInterfaceRight(canvas, text, Width - 68, Height - 47, 22, OffWhite);
        }

        private static void DrawOutlinedDisplay(SKCanvas canvas, string text, float x, float y, float size, SKColor fill, SKColor outline, SKColor shadow)
        {
            using var font = new SKFont(DisplayTypeface.Value, size);
            using var shadowPaint = Paint(shadow);
            canvas.DrawText(text, x + 9, y + 10, SKTextAlign.Left, font, shadowPaint);
            using var stroke = Paint(outline, 12, SKPaintStyle.Stroke);
            stroke.StrokeJoin = SKStrokeJoin.Round;
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, stroke);
            using var fillPaint = Paint(fill);
            canvas.DrawText(text, x, y, SKTextAlign.Left, font, fillPaint);
        }

        private static void DrawDisplayWrapped(SKCanvas canvas, string text, float x, float firstBaseline, float width,
            float initialSize, float lineSpacing, int maxLines, SKColor color, bool shadow)
        {
            float size = initialSize;
            List<string> lines;
            do
            {
                using var measureFont = new SKFont(DisplayTypeface.Value, size);
                lines = Wrap(text, measureFont, width, maxLines);
                if (!lines.Last().EndsWith("…", StringComparison.Ordinal) || size <= 70) break;
                size -= 4;
            } while (true);

            using var font = new SKFont(DisplayTypeface.Value, size);
            using var fill = Paint(color);
            using var shade = Paint(new SKColor(0, 0, 0, 205));
            float y = firstBaseline;
            foreach (string line in lines)
            {
                if (shadow) canvas.DrawText(line, x + 6, y + 7, SKTextAlign.Left, font, shade);
                canvas.DrawText(line, x, y, SKTextAlign.Left, font, fill);
                y += size * lineSpacing;
            }
        }

        private static void DrawInterfaceWrapped(SKCanvas canvas, string text, float x, float firstBaseline, float width,
            float size, float lineSpacing, int maxLines, SKColor color)
        {
            using var font = new SKFont(InterfaceTypeface.Value, size);
            using var paint = Paint(color);
            float y = firstBaseline;
            foreach (string line in Wrap(text, font, width, maxLines))
            {
                canvas.DrawText(line, x, y, SKTextAlign.Left, font, paint);
                y += size * lineSpacing;
            }
        }

        private static List<string> Wrap(string text, SKFont font, float width, int maxLines)
        {
            string[] words = (text ?? string.Empty).Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            int index = 0;
            while (index < words.Length && lines.Count < maxLines)
            {
                string line = string.Empty;
                while (index < words.Length)
                {
                    string candidate = line.Length == 0 ? words[index] : line + " " + words[index];
                    if (line.Length > 0 && font.MeasureText(candidate) > width) break;
                    line = candidate;
                    index++;
                }

                if (line.Length == 0 && index < words.Length) line = words[index++];
                if (lines.Count == maxLines - 1 && index < words.Length) line = line.TrimEnd('.', '…') + "…";
                lines.Add(line);
            }

            if (lines.Count == 0) lines.Add(string.Empty);
            return lines;
        }

        private static void DrawInterface(SKCanvas canvas, string text, float x, float y, float size, SKColor color)
        {
            using var font = new SKFont(InterfaceTypeface.Value, size);
            using var paint = Paint(color);
            canvas.DrawText(text ?? string.Empty, x, y, SKTextAlign.Left, font, paint);
        }

        private static void DrawInterfaceRight(SKCanvas canvas, string text, float right, float y, float size, SKColor color)
        {
            using var font = new SKFont(InterfaceTypeface.Value, size);
            using var paint = Paint(color);
            string safe = text ?? string.Empty;
            canvas.DrawText(safe, right - font.MeasureText(safe), y, SKTextAlign.Left, font, paint);
        }

        private static void DrawInterfaceCentered(SKCanvas canvas, string text, float center, float y, float size, SKColor color)
        {
            using var font = new SKFont(InterfaceTypeface.Value, size);
            using var paint = Paint(color);
            string safe = text ?? string.Empty;
            canvas.DrawText(safe, center - font.MeasureText(safe) / 2f, y, SKTextAlign.Left, font, paint);
        }

        private static string SafeUpper(string value, string fallback) =>
            (string.IsNullOrWhiteSpace(value) ? fallback : value).Trim().ToUpperInvariant();

        private static SKTypeface LoadTypeface(string suffix, string fallback)
        {
            Assembly assembly = typeof(MagazineCoverRenderer).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(candidate => candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name != null)
            {
                using Stream stream = assembly.GetManifestResourceStream(name);
                SKTypeface typeface = stream == null ? null : SKTypeface.FromStream(stream);
                if (typeface != null) return typeface;
            }
            return SKTypeface.FromFamilyName(fallback, SKFontStyle.Normal);
        }

        private static SKBitmap LoadBitmap(string suffix)
        {
            Assembly assembly = typeof(MagazineCoverRenderer).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(candidate => candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
            if (name == null) return null;
            using Stream stream = assembly.GetManifestResourceStream(name);
            return stream == null ? null : SKBitmap.Decode(stream);
        }

        private static SKPaint Paint(SKColor color, float strokeWidth = 1, SKPaintStyle style = SKPaintStyle.Fill) => new()
        {
            Color = color,
            IsAntialias = true,
            StrokeWidth = strokeWidth,
            Style = style
        };

        private static SKRect Offset(SKRect rect, float x, float y) =>
            new(rect.Left + x, rect.Top + y, rect.Right + x, rect.Bottom + y);

        private static void DrawFit(SKCanvas canvas, SKBitmap image, SKRect destination, SKPaint paint = null)
        {
            DrawFit(canvas, image, new SKRect(0, 0, image.Width, image.Height), destination, paint);
        }

        private static void DrawFit(SKCanvas canvas, SKBitmap image, SKRect source, SKRect destination, SKPaint paint = null)
        {
            float scale = Math.Min(destination.Width / source.Width, destination.Height / source.Height);
            float width = source.Width * scale;
            float height = source.Height * scale;
            float left = destination.MidX - width / 2f;
            float top = destination.MidY - height / 2f;
            canvas.DrawBitmap(image, source, new SKRect(left, top, left + width, top + height), paint);
        }
    }
}
