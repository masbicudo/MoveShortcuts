// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ProgramStarter
{
    public sealed record StartupEntry(
        string Path,
        TimeSpan Delay,
        string DisplayName,
        ProcessWindowStyle? WindowStyle);

    public static class StartupEntryParser
    {
        private static readonly Regex DelayPrefix = new(
            @"^(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)_(?<name>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(50));

        public static bool TryParse(string path, ProgramStarterManifest? manifest, out StartupEntry entry, out string? reason)
        {
            var fileName = System.IO.Path.GetFileName(path);
            var nameWithoutExtension = System.IO.Path.GetFileNameWithoutExtension(path);
            var relativePath = fileName;
            var manifestEntry = manifest?.Entries.TryGetValue(relativePath, out var ownedEntry) == true
                ? ownedEntry
                : null;

            var delayText = manifestEntry?.Delay;
            var displayName = nameWithoutExtension;
            var nameMatch = DelayPrefix.Match(nameWithoutExtension);
            if (nameMatch.Success)
                displayName = nameMatch.Groups["name"].Value;

            if (string.IsNullOrWhiteSpace(delayText))
            {
                if (!nameMatch.Success)
                {
                    entry = default!;
                    reason = "name does not start with a delay such as 45s_ or 01m30s_";
                    return false;
                }

                delayText = FormatDelay(
                    nameMatch.Groups["minutes"].Success ? int.Parse(nameMatch.Groups["minutes"].Value) : 0,
                    nameMatch.Groups["seconds"].Success ? int.Parse(nameMatch.Groups["seconds"].Value) : 0);
            }

            if (!TryParseDelay(delayText, out var delay))
            {
                entry = default!;
                reason = $"invalid delay '{delayText}'";
                return false;
            }

            var window = ParseWindowStyle(manifestEntry?.Window);
            entry = new StartupEntry(path, delay, displayName, window);
            reason = null;
            return true;
        }

        public static bool TryParseDelay(string delayText, out TimeSpan delay)
        {
            delay = TimeSpan.Zero;
            var match = Regex.Match(delayText.Trim(), @"^(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
            if (!match.Success || (!match.Groups[1].Success && !match.Groups[2].Success))
                return false;

            var minutes = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
            var seconds = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            if (seconds >= 60)
                return false;

            delay = TimeSpan.FromSeconds(minutes * 60 + seconds);
            return true;
        }

        private static string FormatDelay(int minutes, int seconds)
            => minutes == 0 ? $"{seconds:00}s" : $"{minutes:00}m{seconds:00}s";

        private static ProcessWindowStyle? ParseWindowStyle(string? window)
            => window?.ToLowerInvariant() switch
            {
                "normal" => ProcessWindowStyle.Normal,
                "minimized" => ProcessWindowStyle.Minimized,
                "maximized" => ProcessWindowStyle.Maximized,
                _ => null,
            };
    }

    public sealed class ProgramStarterManifest
    {
        [JsonPropertyName("shortcutsRoot")]
        public string? ShortcutsRoot { get; set; }

        [JsonPropertyName("entries")]
        public Dictionary<string, ProgramStarterManifestEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ProgramStarterManifestEntry
    {
        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("delay")]
        public string? Delay { get; set; }

        [JsonPropertyName("window")]
        public string? Window { get; set; }
    }
}
