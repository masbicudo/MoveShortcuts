// SPDX-License-Identifier: Apache-2.0

using Newtonsoft.Json;
using System.IO;

namespace MoveShortcuts
{
    public static class ResolvedTargetCacheProvider
    {
        public const string DefaultCacheFileName = "move-shortcuts-target-cache.json";

        private static readonly object Gate = new();
        private static string? _loadedPath;
        private static ResolvedTargetCacheFile _cache = new();

        public static string? Resolve(
            string cacheFile,
            string command,
            string argument,
            Func<string, string, string?> resolver,
            Action<string>? log = null)
        {
            var fullPath = Path.GetFullPath(cacheFile);
            var key = ResolvedTargetCacheEntry.CreateKey(command, argument);

            lock (Gate)
            {
                EnsureLoaded(fullPath);
                if (_cache.entries.TryGetValue(key, out var entry) && TargetStillExists(entry.target))
                    return entry.target;
            }

            var target = resolver(command, argument);
            if (string.IsNullOrWhiteSpace(target))
                return target;

            lock (Gate)
            {
                EnsureLoaded(fullPath);
                _cache.entries[key] = new ResolvedTargetCacheEntry
                {
                    command = command,
                    argument = argument,
                    target = target,
                    resolvedUtc = DateTimeOffset.UtcNow,
                };
                TryWrite(fullPath, log);
            }

            return target;
        }

        public static void ResetForTests()
        {
            lock (Gate)
            {
                _loadedPath = null;
                _cache = new();
            }
        }

        private static void EnsureLoaded(string fullPath)
        {
            if (string.Equals(_loadedPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return;

            _loadedPath = fullPath;
            _cache = Read(fullPath);
        }

        private static ResolvedTargetCacheFile Read(string fullPath)
        {
            try
            {
                if (!File.Exists(fullPath))
                    return new();

                var json = File.ReadAllText(fullPath);
                var cache = JsonConvert.DeserializeObject<ResolvedTargetCacheFile>(json) ?? new();
                cache.entries = cache.entries == null
                    ? new(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, ResolvedTargetCacheEntry>(cache.entries, StringComparer.OrdinalIgnoreCase);
                return cache.schemaVersion == 1 ? cache : new();
            }
            catch (JsonException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            return new();
        }

        private static void TryWrite(string fullPath, Action<string>? log)
        {
            try
            {
                _cache.schemaVersion = 1;
                _cache.updatedUtc = DateTimeOffset.UtcNow;

                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrWhiteSpace(directory))
                    Directory.CreateDirectory(directory);

                var tempFile = fullPath + ".tmp";
                File.WriteAllText(tempFile, JsonConvert.SerializeObject(_cache, Formatting.Indented));
                File.Move(tempFile, fullPath, overwrite: true);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                log?.Invoke($"Could not write target cache: {ex.Message}");
            }
        }

        private static bool TargetStillExists(string? target)
            => !string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target));
    }

    public sealed class ResolvedTargetCacheFile
    {
        public int schemaVersion = 1;
        public DateTimeOffset updatedUtc = DateTimeOffset.UtcNow;
        public Dictionary<string, ResolvedTargetCacheEntry> entries = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ResolvedTargetCacheEntry
    {
        public string command = "";
        public string argument = "";
        public string target = "";
        public DateTimeOffset resolvedUtc;

        public static string CreateKey(string command, string argument)
            => $"{command}\u001f{argument}";
    }
}
