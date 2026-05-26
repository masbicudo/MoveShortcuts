// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using System.IO;

namespace MoveShortcuts
{
    public sealed class FileOptionMatcher
    {
        private static readonly char[] RegexMetaCharacters =
        {
            '\\', '.', '^', '$', '|', '?', '*', '+', '(', ')', '[', '{'
        };

        private readonly Dictionary<string, MyFileOptions> _exactOptions;
        private readonly List<(Regex Pattern, MyFileOptions Options)> _patternOptions;

        public FileOptionMatcher(Dictionary<string, MyFileOptions> fileOptions, bool includeFullyQualifiedKeys)
        {
            _exactOptions = new Dictionary<string, MyFileOptions>(StringComparer.OrdinalIgnoreCase);
            foreach (var option in fileOptions)
            {
                if (!_exactOptions.ContainsKey(option.Key))
                    _exactOptions[option.Key] = option.Value;
            }

            _patternOptions = fileOptions
                .Where(kv => (includeFullyQualifiedKeys || !Path.IsPathFullyQualified(kv.Key))
                    && LooksLikeRegex(kv.Key))
                .Select(kv => (new Regex("^" + kv.Key + "$", RegexOptions.IgnoreCase), kv.Value))
                .ToList();
        }

        public bool HasCandidates => _exactOptions.Count > 0 || _patternOptions.Count > 0;

        public bool TryGetOptions(string name, out MyFileOptions? options)
        {
            if (_exactOptions.TryGetValue(name, out options))
                return true;

            foreach (var patternOption in _patternOptions)
            {
                if (patternOption.Pattern.IsMatch(name))
                {
                    options = patternOption.Options;
                    return true;
                }
            }

            options = null;
            return false;
        }

        public static bool LooksLikeRegex(string value)
            => value.IndexOfAny(RegexMetaCharacters) >= 0;
    }
}
