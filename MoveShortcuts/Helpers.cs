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
using System.Xml.Linq;

namespace MoveShortcuts
{
    public enum ProgressMode
    {
        Quiet,
        Log,
        Cli
    }

    public static class Helpers
    {
        private static ProgressMode _progressMode = Console.IsOutputRedirected ? ProgressMode.Log : ProgressMode.Cli;
        private static bool _progressLineActive;
        private static int _progressLineWidth;

        public static string ToString<T>(T o) => $"{o}";

        public static void SetProgressMode(ProgressMode progressMode)
        {
            _progressMode = progressMode;
        }

        public static ProgressMode ResolveProgressMode(string? progress)
        {
            if (string.IsNullOrWhiteSpace(progress) || progress.Equals("auto", StringComparison.OrdinalIgnoreCase))
                return Console.IsOutputRedirected ? ProgressMode.Log : ProgressMode.Cli;

            if (progress.Equals("quiet", StringComparison.OrdinalIgnoreCase))
                return ProgressMode.Quiet;
            if (progress.Equals("log", StringComparison.OrdinalIgnoreCase))
                return ProgressMode.Log;
            if (progress.Equals("cli", StringComparison.OrdinalIgnoreCase))
                return ProgressMode.Cli;

            throw new ArgumentException($"Unknown progress mode '{progress}'. Use quiet, log, or cli.");
        }

        public static void WriteLine(string message)
        {
            ClearProgressLine();
            Console.WriteLine(message);
        }

        public static void Copy(string source, string target)
        {
            var sourceDate = File.GetCreationTimeUtc(source);
            File.Copy(
                source,
                target,
                overwrite: true);
            File.SetCreationTimeUtc(target, sourceDate);
        }

        public static bool WouldShadowExternalCommand(string shortcutsRoot, string outputPath)
            => GetExternalCommandConflict(
                shortcutsRoot,
                outputPath,
                Environment.GetEnvironmentVariable("PATH") ?? "",
                Environment.GetEnvironmentVariable("PATHEXT") ?? "") != null;

        public static bool WouldShadowExternalCommand(
            string shortcutsRoot,
            string outputPath,
            string pathValue,
            string pathExtValue)
            => GetExternalCommandConflict(shortcutsRoot, outputPath, pathValue, pathExtValue) != null;

        public static string? GetExternalCommandConflict(string shortcutsRoot, string outputPath)
            => GetExternalCommandConflict(
                shortcutsRoot,
                outputPath,
                Environment.GetEnvironmentVariable("PATH") ?? "",
                Environment.GetEnvironmentVariable("PATHEXT") ?? "");

        public static string? GetExternalCommandConflict(
            string shortcutsRoot,
            string outputPath,
            string pathValue,
            string pathExtValue)
        {
            var commandName = Path.GetFileNameWithoutExtension(outputPath);
            if (string.IsNullOrWhiteSpace(commandName))
                return null;

            var pathEntries = pathValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var pathext = pathExtValue
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Concat(new[] { ".exe", ".com", ".bat", ".cmd", ".ps1", ".lnk", ".url" })
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidates = new List<string> { commandName };
            candidates.AddRange(pathext.Select(ext => commandName + ext));

            foreach (var dir in pathEntries)
            {
                foreach (var candidate in candidates)
                {
                    var path = Path.Combine(dir, candidate);
                    if (!File.Exists(path))
                        continue;

                    var fullPath = Path.GetFullPath(path);
                    return IsPathInside(shortcutsRoot, fullPath) ? null : fullPath;
                }
            }

            return null;
        }

        public static bool IsPathInside(string root, string path)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        }

        public static bool CopyShortcutOutput(string shortcutsRoot, string source, string target)
        {
            var conflict = GetExternalCommandConflict(shortcutsRoot, target);
            if (conflict != null)
            {
                WriteLine($"Skipping {Path.GetFileName(target)}: conflicts with {conflict}");
                return false;
            }

            return TryWriteShortcutOutput(target, () => Copy(source, target));
        }

        public static bool WriteShortcutOutput(string shortcutsRoot, string target, string contents)
        {
            var conflict = GetExternalCommandConflict(shortcutsRoot, target);
            if (conflict != null)
            {
                WriteLine($"Skipping {Path.GetFileName(target)}: conflicts with {conflict}");
                return false;
            }

            return TryWriteShortcutOutput(target, () => File.WriteAllText(target, contents));
        }

        public static bool CreateShortcutOutput(string shortcutsRoot, string linkfile, string target, string icon = null, string workdir = null)
        {
            var conflict = GetExternalCommandConflict(shortcutsRoot, linkfile);
            if (conflict != null)
            {
                WriteLine($"Skipping {Path.GetFileName(linkfile)}: conflicts with {conflict}");
                return false;
            }

            return CreateShortcut(linkfile, target, icon, workdir);
        }

        private static bool TryWriteShortcutOutput(string target, Action write)
        {
            try
            {
                write();
                return true;
            }
            catch (UnauthorizedAccessException ex)
            {
                WriteLine($"Skipping {Path.GetFileName(target)}: access denied ({ex.Message})");
                return false;
            }
            catch (IOException ex)
            {
                WriteLine($"Skipping {Path.GetFileName(target)}: write failed ({ex.Message})");
                return false;
            }
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
            int counter = 1;
            int total = list.Count();
            foreach (var item in list)
            {
                var msg = $"{counter}/{total}: {transformer(item)}";
                WriteProgress(msg);
                yield return item;
                counter++;
            }
            ClearProgressLine();
        }

        private static void WriteProgress(string message)
        {
            switch (_progressMode)
            {
                case ProgressMode.Quiet:
                    return;
                case ProgressMode.Log:
                    Console.WriteLine(message);
                    return;
                case ProgressMode.Cli:
                    WriteInlineProgress(message);
                    return;
            }
        }

        private static void WriteInlineProgress(string message)
        {
            if (Console.IsOutputRedirected)
            {
                Console.WriteLine(message);
                return;
            }

            try
            {
                var width = Math.Max(Console.BufferWidth - 1, 1);
                if (message.Length > width)
                    message = new string(message.Take(Math.Max(width - 3, 1)).ToArray()) + "...";

                Console.Write("\r" + message);
                var clearWidth = Math.Max(_progressLineWidth - message.Length, 0);
                if (clearWidth > 0)
                    Console.Write(new string(' ', clearWidth));
                Console.Write("\r" + message);
                _progressLineWidth = message.Length;
                _progressLineActive = true;
            }
            catch (IOException)
            {
                Console.WriteLine(message);
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine(message);
            }
        }

        private static void ClearProgressLine()
        {
            if (!_progressLineActive)
                return;

            if (Console.IsOutputRedirected)
            {
                _progressLineActive = false;
                _progressLineWidth = 0;
                return;
            }

            try
            {
                Console.Write("\r" + new string(' ', _progressLineWidth) + "\r");
            }
            catch (IOException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            _progressLineActive = false;
            _progressLineWidth = 0;
        }
        public static Comparison<T> ReverseComparer<T>(Comparison<T> comparer)
        {
            return (x, y) => -comparer(x, y);
        }

        public static bool CreateShortcut(string linkfile, string target, string icon = null, string workdir = null)
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
                        if (target.StartsWith("shell:AppsFolder\\"))
                        {
                            lnk.TargetPath = target;
                            lnk.Arguments = "";
                        }
                        else
                            lnk.TargetPath = target;
                        if (workdir != null) lnk.WorkingDirectory = workdir;
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
                        result.Add((name, appUserModelID));
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
                    var shortcutsRoot = Directory.GetParent(shortcutsFolder)?.FullName ?? shortcutsFolder;
                    Helpers.CreateShortcutOutput(
                        shortcutsRoot,
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
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                        return output; // Returns the first line or null if no output
                    }
                }
            }
            catch (Exception ex)
            {
                // In case of error (e.g., command not found), return null
                WriteLine($"Error: {ex.Message}");
                return null;
            }
        }

        public static string SelfElevatedPs1Template { get; } = """
            # Self-elevate the script if required
            if (-Not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] 'Administrator')) {
                if ([int](Get-CimInstance -Class Win32_OperatingSystem | Select-Object -ExpandProperty BuildNumber) -ge 6000) {
                    $CommandLine = "-File `"" + $MyInvocation.MyCommand.Path + "`" " + $MyInvocation.UnboundArguments
                    Start-Process -FilePath PowerShell.exe -Verb Runas -ArgumentList $CommandLine
                    Exit
                }
            }

            SCRIPT_PLACEHOLDER
            """;

        public static void CreatePowerShellProxy(string altFullPathPs1, string targetObjectToOpen, bool elevated, string? workdir)
        {
            var script = $"& \"{targetObjectToOpen}\" @args";

            if (workdir != null)
            {
                script = $"""
                    cd "{workdir}"
                    {script}
                    """;
            }
            if (elevated)
            {
                script = SelfElevatedPs1Template.Replace("SCRIPT_PLACEHOLDER", script);
            }
            File.WriteAllText(altFullPathPs1, script);
        }

        public static string SelfElevatedCmdTemplate { get; } = """
            :: ----------------------------------------
            :: Self-elevating CMD script with argument forwarding
            :: ----------------------------------------

            :: Function: Check if running as admin
            net session >nul 2>&1
            if %errorLevel% neq 0 (
                echo Requesting administrative privileges...

                :: Build properly quoted argument list
                set "args="
                :loop
                if "%~1"=="" goto endloop
                set "args=%args% \"%~1\""
                shift
                goto loop
                :endloop

                :: Relaunch script elevated with arguments
                powershell -Command "Start-Process '%~f0' -ArgumentList %args% -Verb RunAs"
                exit /b
            )

            :: --- Script continues here as admin ---
            SCRIPT_PLACEHOLDER
            """;

        public static void CreateCommandPromptProxy(string altFullPathCmd, string targetObjectToOpen, bool elevated, string? workdir)
        {
            var script = $"\"{targetObjectToOpen}\" %*";

            if (workdir != null)
            {
                script = $"""
                    cd "{workdir}"
                    {script}
                    """;
            }
            if (elevated)
            {
                script = SelfElevatedCmdTemplate.Replace("SCRIPT_PLACEHOLDER", script);
            }
            script = $"@echo off\r\n{script}";
            File.WriteAllText(altFullPathCmd, script);
        }

        public static string SelfElevatedShTemplate { get; } = """
            #!/usr/bin/env bash
            # ==========================================
            # Self-elevating Git Bash script on Windows
            # ==========================================

            # Function to check if running as admin
            is_admin() {
                # Try a command that requires admin privileges
                net session > /dev/null 2>&1
            }

            if ! is_admin; then
                echo "Requesting administrative privileges..."

                # Build argument string with proper quoting
                args=""
                for arg in "$@"; do
                    # Escape double quotes
                    safe_arg="${arg//\"/\\\"}"
                    args="$args \"$safe_arg\""
                done

                # Relaunch the script elevated via PowerShell
                powershell.exe -Command "Start-Process bash -ArgumentList '-c \"${PWD}/$(basename "$0")$args\"' -Verb RunAs"

                exit 0
            fi

            # --------------------------
            # Script continues here as admin
            # --------------------------
            SCRIPT_PLACEHOLDER
            """;

        public static void CreateGitBashProxy(string altFullPathSh, string targetObjectToOpen, bool elevated, string? workdir)
        {
            var script = $"\"{targetObjectToOpen}\" $@";

            if (workdir != null)
            {
                script = $"""
                    cd "{workdir}"
                    {script}
                    """;
            }
            if (elevated)
            {
                script = SelfElevatedShTemplate.Replace("SCRIPT_PLACEHOLDER", script);
            }
            File.WriteAllText(altFullPathSh, script);
        }
    }
}
