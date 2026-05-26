// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.IO;

namespace MoveShortcuts
{
    public static class ShortcutBinaryInspector
    {
        private static readonly byte[] ShellLinkClsid =
        {
            0x01, 0x14, 0x02, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0xC0, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x46
        };

        public static bool ContainsTargetHint(string linkfile, string target)
        {
            if (!File.Exists(linkfile) || string.IsNullOrWhiteSpace(target))
                return false;

            try
            {
                var bytes = File.ReadAllBytes(linkfile);
                if (!HasShellLinkHeader(bytes))
                    return false;

                foreach (var hint in GetTargetHints(target))
                {
                    if (Contains(bytes, Encoding.Unicode.GetBytes(hint)))
                        return true;
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }

            return false;
        }

        private static bool HasShellLinkHeader(byte[] bytes)
        {
            if (bytes.Length < 0x4C || BitConverter.ToUInt32(bytes, 0) != 0x4C)
                return false;

            for (var i = 0; i < ShellLinkClsid.Length; i++)
            {
                if (bytes[4 + i] != ShellLinkClsid[i])
                    return false;
            }

            return true;
        }

        private static IEnumerable<string> GetTargetHints(string target)
        {
            const string appsFolderPrefix = @"shell:AppsFolder\";
            yield return target;

            if (target.StartsWith(appsFolderPrefix, StringComparison.OrdinalIgnoreCase))
                yield return target[appsFolderPrefix.Length..];
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            if (needle.Length == 0 || haystack.Length < needle.Length)
                return false;

            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var found = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return true;
            }

            return false;
        }
    }
}
