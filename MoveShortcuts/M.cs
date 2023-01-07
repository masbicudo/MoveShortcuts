using System.Text.RegularExpressions;

namespace MoveShortcuts
{
    public static class M
    {
        public static string[] Format(this string[] @this, string pattern, params string[] replacements)
        {
            string[] result = new string[@this.Length * replacements.Length];
            int it = 0;
            foreach (var repl in replacements)
            {
                foreach (var item in @this)
                {
                    result[it++] = Regex.Replace(item, pattern, repl);
                }
            }
            return result;
        }
    }
}
