// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.IO;

namespace MoveShortcuts
{
    public static class StartupMergePlanner
    {
        private static readonly Regex DelayPrefix = new(
            @"^(?:(?<minutes>\d+)m)?(?:(?<seconds>\d+)s)_(?<name>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(50));

        public static StartupMergePlan Plan(
            string startupFolder,
            OwnedOutputManifest manifest,
            string displayName,
            string extension,
            string desiredDelay)
        {
            var identity = GetStartupIdentity(displayName, extension);
            var desiredName = $"{desiredDelay}_{SanitizeStartupName(displayName)}{extension}";
            var desiredPath = Path.Combine(startupFolder, desiredName);
            var unownedConflict = FindUnownedSameIdentity(startupFolder, manifest, identity);
            var unownedConflictName = unownedConflict == null ? null : Path.GetFileName(unownedConflict);
            var unownedConflictDelay = unownedConflict == null ? null : TryGetStartupDelay(unownedConflict);

            if (!manifest.TryFindByIdentity(identity, out var baseRelativePath, out var baseEntry))
            {
                if (unownedConflict != null)
                    return StartupMergePlan.Conflict(
                        identity,
                        desiredPath,
                        $"user-owned startup file already exists ({Path.GetFileName(unownedConflict)})",
                        new ConflictFingerprint(
                            FilePath: unownedConflictName,
                            FileDelay: unownedConflictDelay,
                            OptionsPath: desiredName,
                            OptionsDelay: desiredDelay));

                return StartupMergePlan.Write(identity, desiredPath);
            }

            var basePath = Path.Combine(startupFolder, baseRelativePath);
            var baseDelay = baseEntry.Delay ?? TryGetStartupDelay(baseRelativePath);
            var baseFileExists = File.Exists(basePath);
            var baseFileUnchanged = baseFileExists
                                    && baseDelay != null
                                    && TryGetStartupLogicalIdentity(basePath, out var basePathIdentity)
                                    && basePathIdentity.Equals(identity, StringComparison.OrdinalIgnoreCase)
                                    && TryGetStartupDelay(basePath)?.Equals(baseDelay, StringComparison.OrdinalIgnoreCase) == true;

            if (unownedConflict != null && !unownedConflict.Equals(basePath, StringComparison.OrdinalIgnoreCase))
            {
                if (baseFileUnchanged && desiredDelay.Equals(baseDelay, StringComparison.OrdinalIgnoreCase))
                    return StartupMergePlan.Conflict(
                        identity,
                        desiredPath,
                        $"user-owned startup file already exists ({Path.GetFileName(unownedConflict)})",
                        new ConflictFingerprint(
                            ManifestPath: baseRelativePath,
                            ManifestDelay: baseDelay,
                            FilePath: unownedConflictName,
                            FileDelay: unownedConflictDelay,
                            OptionsPath: desiredName,
                            OptionsDelay: desiredDelay));

                return StartupMergePlan.Conflict(
                    identity,
                    desiredPath,
                    $"startup file changed outside MoveShortcuts ({Path.GetFileName(unownedConflict)}); options also changed",
                    new ConflictFingerprint(
                        ManifestPath: baseRelativePath,
                        ManifestDelay: baseDelay,
                        FilePath: unownedConflictName,
                        FileDelay: unownedConflictDelay,
                        OptionsPath: desiredName,
                        OptionsDelay: desiredDelay));
            }

            if (baseFileExists && !baseFileUnchanged)
                return StartupMergePlan.Conflict(
                    identity,
                    desiredPath,
                    $"owned startup file changed outside MoveShortcuts ({Path.GetFileName(basePath)})",
                    new ConflictFingerprint(
                        ManifestPath: baseRelativePath,
                        ManifestDelay: baseDelay,
                        FilePath: Path.GetFileName(basePath),
                        FileDelay: TryGetStartupDelay(basePath),
                        OptionsPath: desiredName,
                        OptionsDelay: desiredDelay));

            if (!baseFileExists && unownedConflict == null)
                return StartupMergePlan.Write(identity, desiredPath, oldOwnedPath: basePath);

            if (desiredPath.Equals(basePath, StringComparison.OrdinalIgnoreCase))
                return StartupMergePlan.Write(identity, desiredPath);

            if (baseFileUnchanged)
                return StartupMergePlan.Write(identity, desiredPath, oldOwnedPath: basePath);

            return StartupMergePlan.Conflict(
                identity,
                desiredPath,
                $"could not merge startup entry for {displayName}",
                new ConflictFingerprint(
                    ManifestPath: baseRelativePath,
                    ManifestDelay: baseDelay,
                    FilePath: baseFileExists ? Path.GetFileName(basePath) : null,
                    FileDelay: baseFileExists ? TryGetStartupDelay(basePath) : null,
                    OptionsPath: desiredName,
                    OptionsDelay: desiredDelay));
        }

        public static string GetStartupIdentity(string displayName, string extension)
            => $"startup:{displayName}{extension}".ToLowerInvariant();

        public static bool TryGetStartupLogicalIdentity(string path, out string identity)
        {
            identity = "";
            var name = Path.GetFileNameWithoutExtension(path);
            var match = DelayPrefix.Match(name);
            if (!match.Success)
                return false;

            identity = GetStartupIdentity(match.Groups["name"].Value, Path.GetExtension(path));
            return true;
        }

        public static string? TryGetStartupDelay(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            var match = DelayPrefix.Match(name);
            if (!match.Success)
                return null;

            var minutes = match.Groups["minutes"].Success ? int.Parse(match.Groups["minutes"].Value) : 0;
            var seconds = match.Groups["seconds"].Success ? int.Parse(match.Groups["seconds"].Value) : 0;
            return minutes == 0 ? $"{seconds:00}s" : $"{minutes:00}m{seconds:00}s";
        }

        public static string SanitizeStartupName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name.Trim();
        }

        private static string? FindUnownedSameIdentity(string startupFolder, OwnedOutputManifest manifest, string identity)
        {
            if (!Directory.Exists(startupFolder))
                return null;

            foreach (var path in Directory.EnumerateFiles(startupFolder))
            {
                if (manifest.IsOwned(path))
                    continue;
                if (!TryGetStartupLogicalIdentity(path, out var candidateIdentity))
                    continue;
                if (candidateIdentity.Equals(identity, StringComparison.OrdinalIgnoreCase))
                    return path;
            }

            return null;
        }
    }

    public sealed record StartupMergePlan(
        StartupMergeStatus Status,
        string Identity,
        string TargetPath,
        string? OldOwnedPath,
        string? Message,
        ConflictFingerprint? Fingerprint)
    {
        public static StartupMergePlan Write(string identity, string targetPath, string? oldOwnedPath = null)
            => new(StartupMergeStatus.Write, identity, targetPath, oldOwnedPath, null, null);

        public static StartupMergePlan Conflict(string identity, string targetPath, string message, ConflictFingerprint fingerprint)
            => new(StartupMergeStatus.Conflict, identity, targetPath, null, message, fingerprint);
    }

    public enum StartupMergeStatus
    {
        Write,
        Conflict
    }
}
