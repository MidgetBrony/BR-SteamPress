using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BR_SteamPress
{
    internal static class SteamLibraryDiscovery
    {
        private static readonly Regex QuotedPair = new("\"(?<key>[^\"]+)\"\\s+\"(?<value>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);

        internal static IReadOnlyList<SteamGame> FindInstalledGames()
        {
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddIfSteamRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            AddIfSteamRoot(roots, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"));
            AddContainingSteamRoot(roots, AppContext.BaseDirectory);
            AddContainingSteamRoot(roots, Environment.CurrentDirectory);

            foreach (string root in roots.ToArray())
                ReadLibraryFolders(root, roots);

            var games = new Dictionary<uint, SteamGame>();
            foreach (string root in roots)
            {
                string steamApps = Path.Combine(root, "steamapps");
                if (!Directory.Exists(steamApps)) continue;
                foreach (string manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly))
                {
                    SteamGame game = ReadManifest(manifest);
                    if (game != null) games[game.AppId] = game;
                }
            }

            return games.Values
                .OrderByDescending(game => game.LastUpdated)
                .ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddIfSteamRoot(ISet<string> roots, string path)
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(Path.Combine(path, "steamapps")))
                roots.Add(Path.GetFullPath(path));
        }

        private static void AddContainingSteamRoot(ISet<string> roots, string path)
        {
            try
            {
                DirectoryInfo current = new(path);
                while (current != null)
                {
                    if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase) && current.Parent != null)
                    {
                        AddIfSteamRoot(roots, current.Parent.FullName);
                        return;
                    }
                    current = current.Parent;
                }
            }
            catch { }
        }

        private static void ReadLibraryFolders(string steamRoot, ISet<string> roots)
        {
            string file = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(file)) return;
            try
            {
                foreach (Match match in QuotedPair.Matches(File.ReadAllText(file)))
                {
                    if (!string.Equals(match.Groups["key"].Value, "path", StringComparison.OrdinalIgnoreCase)) continue;
                    string value = match.Groups["value"].Value.Replace("\\\\", "\\");
                    AddIfSteamRoot(roots, value);
                }
            }
            catch { }
        }

        private static SteamGame ReadManifest(string path)
        {
            try
            {
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match match in QuotedPair.Matches(File.ReadAllText(path)))
                    values[match.Groups["key"].Value] = match.Groups["value"].Value.Replace("\\\"", "\"");

                if (!values.TryGetValue("appid", out string appIdText) || !uint.TryParse(appIdText, out uint appId)) return null;
                values.TryGetValue("name", out string name);
                values.TryGetValue("lastupdated", out string updatedText);
                long.TryParse(updatedText, out long updated);
                return new SteamGame { AppId = appId, Name = string.IsNullOrWhiteSpace(name) ? $"Steam App {appId}" : name, LastUpdated = updated };
            }
            catch { return null; }
        }
    }
}
