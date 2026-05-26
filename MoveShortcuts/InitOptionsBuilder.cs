// SPDX-License-Identifier: Apache-2.0

using System.IO;
using System.Text.RegularExpressions;

namespace MoveShortcuts
{
    public static class InitOptionsBuilder
    {
        public static MyFileOptions CreateFileOptions(bool deleteDesktopShortcuts)
        {
            var action = FileAction.MakeShortcut
                | FileAction.FolderLink
                | FileAction.InternetLink
                | FileAction.FileLink;

            if (deleteDesktopShortcuts)
                action |= FileAction.DeleteDesktopLink;

            return new MyFileOptions { Action = action };
        }

        public static string GenerateInitialsAlias(string name)
        {
            var words = Regex.Matches(name, @"[A-Z]+(?=[A-Z][a-z]|\b)|[A-Z]?[a-z]+|\d+")
                .Select(match => match.Value)
                .Where(word => word.Any(char.IsLetterOrDigit))
                .ToList();

            return string.Concat(words.Select(word => char.ToLowerInvariant(word[0])));
        }

        public static InitAliasResult TryAddInitialsAlias(
            MyFileOptions option,
            string name,
            HashSet<string> reservedNames,
            string shortcutsRoot,
            int minimumLength)
        {
            var alias = GenerateInitialsAlias(name);
            if (string.IsNullOrWhiteSpace(alias) || alias.Length < minimumLength)
                return new InitAliasResult(alias, InitAliasStatus.NotGenerated);

            if (alias.Equals(name, StringComparison.OrdinalIgnoreCase))
                return new InitAliasResult(alias, InitAliasStatus.Skipped);

            if (!reservedNames.Add(alias))
                return new InitAliasResult(alias, InitAliasStatus.Skipped);

            if (Helpers.GetExternalCommandConflict(shortcutsRoot, Path.Combine(shortcutsRoot, alias + ".lnk")) != null)
            {
                reservedNames.Remove(alias);
                return new InitAliasResult(alias, InitAliasStatus.Skipped);
            }

            option.AltNames.Add(alias);
            return new InitAliasResult(alias, InitAliasStatus.Added);
        }

        public static void ApplyGlobalCleanupSetting(Settings options)
        {
            foreach (var opts in options.fileOptions.Values)
            {
                if (options.cleanup.deleteDesktopShortcuts)
                    opts.Action |= FileAction.DeleteDesktopLink;
                else
                    opts.Action &= ~FileAction.DeleteDesktopLink;
            }
        }

        public static void NormalizeSettings(Settings options)
        {
            options.sources ??= new SourceSettings();
            options.cleanup ??= new CleanupSettings();
            options.aliases ??= new AliasSettings();
            options.path ??= new PathSettings();
            options.fileOptions ??= new Dictionary<string, MyFileOptions>();
        }
    }

    public record InitAliasResult(string Alias, InitAliasStatus Status);

    public enum InitAliasStatus
    {
        NotGenerated,
        Added,
        Skipped
    }
}
