using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

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
            foreach (var item in list)
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

        public static bool CreateShortcut(string linkfile, string target, string icon = null)
        {
            Type t = Type.GetTypeFromCLSID(new Guid("72C24DD5-D70A-438B-8A42-98424B88AFB8")); //Windows Script Host Shell Object
            dynamic shell = Activator.CreateInstance(t);
            try
            {
                try
                {
                    var lnk = shell.CreateShortcut(linkfile);
                    try
                    {
                        lnk.TargetPath = target;
                        if (icon != null) lnk.IconLocation = icon;
                        lnk.Save();
                        return true;
                    }
                    finally
                    {
                        Marshal.FinalReleaseComObject(lnk);
                    }
                }
                finally
                {
                    Marshal.FinalReleaseComObject(shell);
                }
            }
            catch (Exception ex)
            {
                return false;
            }
        }


        public static Dictionary<string, string> GetUwpApps()
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = "..\\..\\..\\..\\CreateUWPList\\bin\\Debug\\net7.0\\CreateUWPList.exe";
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            Process process = new Process();
            process.StartInfo = startInfo;
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();

            process.WaitForExit();

            int exitCode = process.ExitCode;

            var appNames = new Dictionary<string, string>();
            var outputLines = output.Split("\r\n");
            for (int it = 0; it < outputLines.Length; it += 2)
            {
                if (outputLines[it + 0] == "") break;
                appNames[outputLines[it + 0]] = outputLines[it + 1];
            }

            return appNames;
        }

        public static void CreateUWPShortcuts(
                string shortcutsFolder,
                Dictionary<string, MyFileOptions> fileOptions,
                Dictionary<string, string> appNames
            )
        {
            int it = 0;
            foreach (var kv in Helpers.LogProgress(appNames))
            {
                string name = kv.Key;
                string appUserModelID = kv.Value;
                if (!fileOptions.TryGetValue(name, out MyFileOptions? opts))
                {
                    var keyMatch = fileOptions.Keys
                        .Where(x => !Path.IsPathFullyQualified(x))
                        .FirstOrDefault(
                            k => Regex.IsMatch(name, "^" + k + "$", RegexOptions.IgnoreCase));
                    if (keyMatch != null)
                        opts = fileOptions[keyMatch];
                }
                if (opts != null)
                {
                    var fullPath = Path.Combine(shortcutsFolder, name + ".lnk");
                    Directory.CreateDirectory(shortcutsFolder);
                    Helpers.CreateShortcut(
                        fullPath,
                        @$"shell:AppsFolder\{appUserModelID}"
                        );
                }
                it++;
            }
        }

        public static string GetFavIconName(string url, string name = "/favicon.ico")
        {
            Uri myUri = new Uri(url);
            string schema_host = myUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);  // host is "www.contoso.com"
            return schema_host + name;
        }
        public static byte[] Download(string url)
        {
            Uri myUri = new Uri(url);
            string host = myUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);  // host is "www.contoso.com"
            WebRequest request = (HttpWebRequest)WebRequest.Create(host + "/favicon.ico");
            using (WebResponse response = request.GetResponse())
            using (Stream responseStream = response.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                responseStream.CopyTo(ms);
                return ms.ToArray();
            }
        }
    }
}
