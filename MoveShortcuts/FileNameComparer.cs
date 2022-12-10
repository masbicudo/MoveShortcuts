// See https://aka.ms/new-console-template for more information

using System.Text.RegularExpressions;

namespace MoveShortcuts
{
    public class FileNameComparer :
        IComparer<string>
    {
        private static readonly Regex rgx = new(@"(\d+)", RegexOptions.Compiled, TimeSpan.FromMicroseconds(10));
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return +1;

            static string[] SplitFileName(string fn) => rgx.Split(fn).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();

            var xs = SplitFileName(x);
            var ys = SplitFileName(y);

            var maxLen = Math.Max(xs.Length, ys.Length);
            Array.Resize(ref xs, maxLen);
            Array.Resize(ref ys, maxLen);

            for (int it = 0; it < maxLen; it++)
            {
                var xi = xs[it];
                var yi = ys[it];
                if (xi == null) return -1;
                if (yi == null) return +1;

                int result;
                if (int.TryParse(xi, out int xn) && int.TryParse(yi, out int yn))
                    result = Comparer<int>.Default.Compare(xn, yn);
                else
                    result = StringComparer.InvariantCultureIgnoreCase.Compare(xi, yi);

                if (result != 0)
                    return result;
            }

            return 0;
        }

        public static readonly FileNameComparer Default = new();
    }
}