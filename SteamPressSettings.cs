using MelonLoader;
using ModsPanel;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BR_SteamPress
{
    internal static class SteamPressSettings
    {
        private static MelonPreferences_Entry<bool> enabled;
        private static MelonPreferences_Entry<bool> includeSteam;
        private static MelonPreferences_Entry<string> appIds;
        private static MelonPreferences_Entry<string> feedUrls;
        private static MelonPreferences_Entry<int> maxStories;
        private static MelonPreferences_Entry<int> lookbackDays;
        private static MelonPreferences_Entry<int> maximumGames;

        internal static bool Enabled => enabled?.Value ?? true;
        internal static bool IncludeSteam => includeSteam?.Value ?? true;
        internal static int MaxStories => Math.Max(1, Math.Min(24, maxStories?.Value ?? 10));
        internal static int LookbackDays => Math.Max(1, Math.Min(30, lookbackDays?.Value ?? 7));
        internal static int MaximumGames => Math.Max(1, Math.Min(250, maximumGames?.Value ?? 80));

        internal static void Initialize(Action publishNow)
        {
            MelonPreferences_Category category = MelonPreferences.CreateCategory("BRSteamPress", "BR-SteamPress");
            enabled = category.CreateEntry("Enabled", true, "Publish a daily issue on startup");
            includeSteam = category.CreateEntry("IncludeSteam", true, "Include news for Steam games");
            appIds = category.CreateEntry("AppIds", string.Empty, "Optional comma-separated Steam AppIDs");
            feedUrls = category.CreateEntry("FeedUrls", string.Empty, "Optional semicolon-separated RSS or Atom feed URLs");
            maxStories = category.CreateEntry("MaxStories", 10, "Maximum stories per issue");
            lookbackDays = category.CreateEntry("LookbackDays", 7, "How far back to search for news");
            maximumGames = category.CreateEntry("MaximumGames", 80, "Maximum installed games checked per run");

            ModsPanelApi.RegisterSection("Rusty.BR-SteamPress", "BR-SteamPress", 110)
                .Clear()
                .AddLabel("description", "Publishes Steam news and ordinary RSS/Atom feeds as dated magazines in your BR-BookSystem library.")
                .AddToggle("enabled", "Publish today's issue on startup", () => enabled.Value, value => { enabled.Value = value; Save(); })
                .AddToggle("steam", "Include Steam game news", () => includeSteam.Value, value => { includeSteam.Value = value; Save(); })
                .AddText("appids", "Steam AppIDs", () => appIds.Value ?? string.Empty, value => { appIds.Value = value ?? string.Empty; Save(); }, "570, 440, 620")
                .AddText("feeds", "RSS / Atom feed URLs", () => feedUrls.Value ?? string.Empty, value => { feedUrls.Value = value ?? string.Empty; Save(); }, "https://example.com/feed.xml; https://example.org/atom.xml")
                .AddSlider("stories", "Stories per issue", () => MaxStories, value => { maxStories.Value = (int)value; Save(); }, 1, 24, true, "0")
                .AddSlider("days", "News lookback (days)", () => LookbackDays, value => { lookbackDays.Value = (int)value; Save(); }, 1, 30, true, "0")
                .AddSlider("games", "Installed games checked", () => MaximumGames, value => { maximumGames.Value = (int)value; Save(); }, 10, 250, true, "0")
                .AddButton("publish", "Create or update today's issue", "Publish Now", publishNow);
        }

        internal static IReadOnlyList<uint> SelectedAppIds()
        {
            return (appIds?.Value ?? string.Empty)
                .Split(new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint parsed) ? parsed : 0)
                .Where(value => value > 0)
                .Distinct()
                .ToArray();
        }

        internal static IReadOnlyList<string> FeedUrls()
        {
            return (feedUrls?.Value ?? string.Empty)
                .Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => Uri.TryCreate(value, UriKind.Absolute, out Uri uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(32)
                .ToArray();
        }

        private static void Save() => MelonPreferences.Save();
    }
}
