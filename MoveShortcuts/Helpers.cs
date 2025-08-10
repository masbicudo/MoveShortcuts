using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            var appNames = new Dictionary<string, string>();
            foreach (var (name, appUserModelID) in GetUwpAppsInternal())
                appNames[name] = ("X" + appUserModelID).Substring(1);

            return appNames;
        }
        private static List<(string Name, string AppUserModelID)> GetUwpAppsInternal()
        {
            var result = new List<(string Name, string AppUserModelID)>();

            // Adding actions for UWP apps that are installed
            // GUID taken from https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid
            var FODLERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
            try
            {
                using (ShellObject appsFolder = (ShellObject)KnownFolderHelper.FromKnownFolderId(FODLERID_AppsFolder))
                {
                    Dictionary<string, string> appNames = new();
                    foreach (var app in (IKnownFolder)appsFolder)
                    {
                        string name = app.Name;
                        string appUserModelID = app.ParsingName;
                        result.Add((app.Name, app.ParsingName));
                    }
                }
            }
            catch (TypeLoadException)
            {
            }
            return result;
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
        public static byte[]? Download(string url)
        {
            Uri myUri = new Uri(url);
            string host = myUri.GetComponents(UriComponents.SchemeAndServer, UriFormat.Unescaped);  // host is "www.contoso.com"
            WebRequest request = (HttpWebRequest)WebRequest.Create(host + "/favicon.ico");
            try
            {
                using (WebResponse response = request.GetResponse())
                using (Stream responseStream = response.GetResponseStream())
                using (MemoryStream ms = new MemoryStream())
                {
                    responseStream.CopyTo(ms);
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static string RunCommandAndGetFirstLine(string command, string argument)
        {
            try
            {
                // Set up the process start info
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = command,             // The command to run, e.g., "es"
                    Arguments = argument,           // The argument, e.g., the filename
                    RedirectStandardOutput = true,  // To capture the output
                    UseShellExecute = false,        // Do not use shell execution to allow redirection
                    CreateNoWindow = true           // Prevents creating a command window
                };

                using (var process = Process.Start(processStartInfo))  // Start the process
                {
                    using (var reader = process.StandardOutput)  // Read the output
                    {
                        // Read the first line of output
                        string output = reader.ReadLine();
                        return output; // Returns the first line or null if no output
                    }
                }
            }
            catch (Exception ex)
            {
                // In case of error (e.g., command not found), return null
                Console.WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

    }
}
