// SPDX-License-Identifier: Apache-2.0

using Newtonsoft.Json;
using System.IO;

namespace MoveShortcuts
{
    public sealed class OwnedOutputManifest
    {
        private readonly string _manifestPath;
        private readonly string _root;
        private readonly Dictionary<string, OwnedOutputEntry> _entries;
        private readonly HashSet<string> _touched = new(StringComparer.OrdinalIgnoreCase);

        private OwnedOutputManifest(string manifestPath, string root, OwnedOutputManifestFile file, bool manifestExisted)
        {
            _manifestPath = manifestPath;
            _root = Path.GetFullPath(root);
            File = file;
            ManifestExisted = manifestExisted;
            _entries = new Dictionary<string, OwnedOutputEntry>(
                file.Entries ?? new Dictionary<string, OwnedOutputEntry>(),
                StringComparer.OrdinalIgnoreCase);
            File.Entries = _entries;
            File.IgnoredConflicts = new Dictionary<string, List<IgnoredConflictEntry>>(
                File.IgnoredConflicts ?? new Dictionary<string, List<IgnoredConflictEntry>>(),
                StringComparer.OrdinalIgnoreCase);
        }

        public OwnedOutputManifestFile File { get; }
        public bool ManifestExisted { get; }
        public bool AutoIgnoreConflicts { get; set; }

        public IReadOnlyCollection<string> TouchedRelativePaths => _touched;
        public IReadOnlyDictionary<string, OwnedOutputEntry> Entries => _entries;

        public static OwnedOutputManifest Load(string root, string manifestFileName, string owner)
        {
            Directory.CreateDirectory(root);
            var manifestPath = Path.Combine(root, manifestFileName);
            OwnedOutputManifestFile file = new();
            var manifestExisted = System.IO.File.Exists(manifestPath);
            if (manifestExisted)
            {
                try
                {
                    file = JsonConvert.DeserializeObject<OwnedOutputManifestFile>(
                        System.IO.File.ReadAllText(manifestPath)) ?? new();
                }
                catch (JsonException)
                {
                    file = new();
                }
                catch (IOException)
                {
                    file = new();
                }
            }

            file.Owner = owner;
            file.SchemaVersion = Math.Max(file.SchemaVersion, 1);
            return new OwnedOutputManifest(manifestPath, root, file, manifestExisted);
        }

        public bool CanWrite(string path)
        {
            var relativePath = GetRelativePath(path);
            return !System.IO.File.Exists(path) || _entries.ContainsKey(relativePath);
        }

        public bool CanWriteOrHandleConflict(string path, string action = "Skipping")
        {
            if (CanWrite(path))
            {
                ClearIgnoredConflicts(GetOutputIdentity(path));
                return true;
            }

            var identity = GetOutputIdentity(path);
            var relativePath = GetRelativePath(path);
            var fingerprint = new ConflictFingerprint(
                FilePath: relativePath,
                OptionsPath: relativePath);

            if (IsIgnoredConflict(identity, fingerprint))
                return false;

            ClearIgnoredConflicts(identity);
            if (AutoIgnoreConflicts)
            {
                IgnoreConflict(identity, fingerprint);
                Helpers.WriteLine($"Ignoring output conflict for {relativePath}: file exists but is not owned by MoveShortcuts.");
            }
            else
            {
                Helpers.WriteLine($"{action} {relativePath}: file exists but is not owned by MoveShortcuts.");
            }

            return false;
        }

        public bool IsOwned(string path)
            => _entries.ContainsKey(GetRelativePath(path));

        public string GetOutputIdentity(string path)
            => "output:" + GetRelativePath(path).Replace(Path.DirectorySeparatorChar, '/').ToLowerInvariant();

        public bool TryFindByIdentity(string identity, out string relativePath, out OwnedOutputEntry entry)
        {
            foreach (var kv in _entries)
            {
                if (kv.Value.Identity != null && kv.Value.Identity.Equals(identity, StringComparison.OrdinalIgnoreCase))
                {
                    relativePath = kv.Key;
                    entry = kv.Value;
                    return true;
                }
            }

            relativePath = "";
            entry = null!;
            return false;
        }

        public bool IsIgnoredConflict(string identity, ConflictFingerprint? fingerprint)
        {
            if (fingerprint == null)
                return false;
            if (!File.IgnoredConflicts.TryGetValue(identity, out var ignored))
                return false;

            return ignored.Any(entry => entry.Fingerprint.Equals(fingerprint));
        }

        public void IgnoreConflict(string identity, ConflictFingerprint fingerprint)
        {
            if (!File.IgnoredConflicts.TryGetValue(identity, out var ignored))
            {
                ignored = new List<IgnoredConflictEntry>();
                File.IgnoredConflicts[identity] = ignored;
            }

            if (ignored.Any(entry => entry.Fingerprint.Equals(fingerprint)))
                return;

            ignored.Add(new IgnoredConflictEntry
            {
                Fingerprint = fingerprint,
                IgnoredAtUtc = DateTimeOffset.UtcNow,
            });
        }

        public void ClearIgnoredConflicts(string identity)
        {
            File.IgnoredConflicts.Remove(identity);
        }

        public bool TryClaimWrite(string path, string? source = null)
        {
            var relativePath = GetRelativePath(path);
            if (System.IO.File.Exists(path) && !_entries.ContainsKey(relativePath))
                return false;

            Touch(path, source);
            return true;
        }

        public OwnedOutputEntry Touch(string path, string? source = null, string? identity = null)
        {
            var relativePath = GetRelativePath(path);
            if (!_entries.TryGetValue(relativePath, out var entry))
            {
                entry = new OwnedOutputEntry();
                _entries[relativePath] = entry;
            }

            entry.Source = source;
            entry.Identity = identity ?? entry.Identity;
            entry.LastSeenUtc = DateTimeOffset.UtcNow;
            _touched.Add(relativePath);
            return entry;
        }

        public OwnedOutputEntry AdoptExisting(string path, string? source = null, string? identity = null)
        {
            if (!System.IO.File.Exists(path))
                throw new FileNotFoundException("Cannot adopt a missing file.", path);

            return Touch(path, source, identity);
        }

        public bool RemoveStaleTouchedScope(Func<string, bool>? shouldConsider = null)
        {
            var removedAny = false;
            foreach (var relativePath in _entries.Keys.ToList())
            {
                if (_touched.Contains(relativePath))
                    continue;
                if (shouldConsider != null && !shouldConsider(relativePath))
                    continue;

                var fullPath = Path.Combine(_root, relativePath);
                try
                {
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                        removedAny = true;
                    }
                    _entries.Remove(relativePath);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Helpers.WriteLine($"Could not remove stale owned file {relativePath}: access denied ({ex.Message})");
                }
                catch (IOException ex)
                {
                    Helpers.WriteLine($"Could not remove stale owned file {relativePath}: delete failed ({ex.Message})");
                }
            }

            return removedAny;
        }

        public void Save()
        {
            File.GeneratedAtUtc = DateTimeOffset.UtcNow;
            var json = JsonConvert.SerializeObject(File, Formatting.Indented);
            System.IO.File.WriteAllText(_manifestPath, json);
        }

        private string GetRelativePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (!Helpers.IsPathInside(_root, fullPath) &&
                !string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar), _root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{fullPath} is outside {_root}");

            return Path.GetRelativePath(_root, fullPath);
        }
    }

    public sealed class OwnedOutputManifestFile
    {
        public int SchemaVersion { get; set; } = 1;
        public string Owner { get; set; } = "";
        public string? ShortcutsRoot { get; set; }
        public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<string, OwnedOutputEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<IgnoredConflictEntry>> IgnoredConflicts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class OwnedOutputEntry
    {
        public string? Identity { get; set; }
        public string? Source { get; set; }
        public string? Delay { get; set; }
        public string? Window { get; set; }
        public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class IgnoredConflictEntry
    {
        public ConflictFingerprint Fingerprint { get; set; } = new();
        public DateTimeOffset IgnoredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed record ConflictFingerprint(
        string? ManifestPath = null,
        string? ManifestDelay = null,
        string? FilePath = null,
        string? FileDelay = null,
        string? OptionsPath = null,
        string? OptionsDelay = null);
}
