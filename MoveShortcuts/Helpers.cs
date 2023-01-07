using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MoveShortcuts
{
}

namespace MoveShortcuts
{
    public static class Helpers
    {
        public static string ToString<T>(T o) => $"{o}";

        public static void Copy(string source, string target)
        {
            var sourceDate = File.GetCreationTimeUtc(source);
            File.Copy(
                source,
                target,
                overwrite: true);
            File.SetCreationTimeUtc(target, sourceDate);
        }

        public static bool HasExt(string filename, params string[] extensions)
        {
            bool result = extensions.Contains(
                    Path.GetExtension(filename),
                    StringComparer.InvariantCultureIgnoreCase);
            return result;
        }

        public static void MakeElevatedLink(string altFullPath)
        {
            byte[] fileBytes = File.ReadAllBytes(altFullPath);
            fileBytes[21] = 32;
            File.WriteAllBytes(altFullPath, fileBytes);
        }

        public static IEnumerable<T> LogProgress<T>(IEnumerable<T> list, Func<T, string>? transformer = null)
        {
            if (transformer == null)
                transformer = ToString<T>;
            int width = Console.BufferWidth;
            int counter = 1;
            int total = list.Count();
            foreach(var item in list)
            {
                var (left, top) = Console.GetCursorPosition();
                var msg = $"{counter}/{total}: {transformer(item)}";
                if (msg.Length > width)
                {
                    msg = new string(msg.Take(width - 3).ToArray()) + "...";
                }
                else
                {
                    msg = msg + new string(' ', width - msg.Length);
                }
                Console.Write(msg);
                Console.SetCursorPosition(left, top);
                yield return item;
                counter++;
            }
            Console.WriteLine();
        }
        public static Comparison<T> ReverseComparer<T>(Comparison<T> comparer)
        {
            return (x, y) => -comparer(x, y);
        }
    }
}
