// See https://aka.ms/new-console-template for more information
using HtmlAgilityPack;
using MoveShortcuts;
using Newtonsoft.Json;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

var log = new StreamWriter(File.Open("move-shortcuts.log", FileMode.Append));

var options = new Settings();

var optsFileName = "move-shortcuts-options.json";
if (File.Exists(optsFileName))
{
    var json = File.ReadAllText(optsFileName);
    options = JsonConvert.DeserializeObject<Settings>(json);
}
else
{
    var json = JsonConvert.SerializeObject(options, Formatting.Indented);
    File.WriteAllText(optsFileName, json);
}

var fileOptions = options.fileOptions;
var shortcuts = options.shortcuts;

// ensuring shortcuts directory is there
Directory.CreateDirectory(shortcuts);

//
// Collecting files to process
//
var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
var desktopPath2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
var startMenuPath2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

//
// First step: listing files from special locations
//
var dirEnumOptsRecursive = new EnumerationOptions
{
    IgnoreInaccessible = true,
    RecurseSubdirectories = true,
};
var dirEnumOpts = new EnumerationOptions
{
    IgnoreInaccessible = true,
};
List<string> allSourceFiles = new();

// Creating shortcuts for all UWP programs that appear in the
// listing inside the configurations file. These will be created
// in a subfolder of the shortcuts folder
Console.WriteLine("Creating shortcuts for UWP programs:");
var uwpApps = Helpers.GetUwpApps();
var uwpShortcutsFolder = Path.Combine(shortcuts, "UWP Apps");
Helpers.CreateUWPShortcuts(uwpShortcutsFolder, fileOptions, uwpApps);

Console.WriteLine("Collecting items locations");
{
    //      We use a reverse filename comparer so that the
    // files with greatest versions comes first and we end up
    // using these to create shortcuts.
    //      This only happens if the files have the same name
    // but are contained in folders that carry version numbers.
    // When the version number is directly on the final file,
    // then both will get to the next stage, where the sorting
    // is in ascending order. This is made because every file
    // will be processed, and we want larger versions to overwrite
    // lesser versions associated commands.
    var revFileNameComparer = Helpers.ReverseComparer<string>(FileNameComparer.Default.Compare);

    var allDesktopFiles = Directory.GetFiles(desktopPath, "*.*", dirEnumOpts).ToList();
    allDesktopFiles.Sort(revFileNameComparer);
    allSourceFiles.AddRange(allDesktopFiles);

    var allDesktopFiles2 = Directory.GetFiles(desktopPath2, "*.*", dirEnumOpts).ToList();
    allDesktopFiles2.Sort(revFileNameComparer);
    allSourceFiles.AddRange(allDesktopFiles2);

    var allStartMenuFiles = Directory.GetFiles(startMenuPath, "*.*", dirEnumOptsRecursive).ToList();
    allStartMenuFiles.Sort(revFileNameComparer);
    allSourceFiles.AddRange(allStartMenuFiles);

    var allStartMenuFiles2 = Directory.GetFiles(startMenuPath2, "*.*", dirEnumOptsRecursive).ToList();
    allStartMenuFiles2.Sort(revFileNameComparer);
    allSourceFiles.AddRange(allStartMenuFiles2);

    var allUwpApps = Directory.GetFiles(uwpShortcutsFolder, "*.*", dirEnumOptsRecursive).ToList();
    allUwpApps.Sort(revFileNameComparer);
    allSourceFiles.AddRange(allUwpApps);

}
Console.WriteLine($"  Found {allSourceFiles.Count} items");

//
// Second step: matching collected files with items from the settings
//
Console.WriteLine("Collecting files to process:");
HashSet<string> usedFilesNames = new();
List<MyFileAction> actionsList = new();

foreach (var file in Helpers.LogProgress(allSourceFiles, Path.GetFileName))
{
    var fileName = Path.GetFileNameWithoutExtension(file);
    if (usedFilesNames.Contains(fileName))
        continue;
    usedFilesNames.Add(fileName);
    if (!fileOptions.TryGetValue(fileName, out MyFileOptions? opts))
    {
        var keyMatch = fileOptions.Keys
            .Where(x => !Path.IsPathFullyQualified(x))
            .FirstOrDefault(
                k => Regex.IsMatch(fileName, "^" + k + "$", RegexOptions.IgnoreCase));
        if (keyMatch != null)
            opts = fileOptions[keyMatch];
    }
    var fileAttr = (FILE_ATTRIBUTES)File.GetAttributes(file);
    var isOffline = (fileAttr & FILE_ATTRIBUTES.OFFLINE) != 0;
    var isRecallOnAccess = (fileAttr & FILE_ATTRIBUTES.RECALL_ON_DATA_ACCESS) != 0;
    if (opts != null)
    {
        actionsList.Add(new(file, fileName, opts, isOffline || isRecallOnAccess));
    }
}

foreach (var file in fileOptions.Keys.Where(Path.IsPathFullyQualified))
{
    var fileName = Path.GetFileNameWithoutExtension(file);
    var opts = fileOptions[file];
    var fileAttr = (FILE_ATTRIBUTES)File.GetAttributes(file);
    var isOffline = (fileAttr & FILE_ATTRIBUTES.OFFLINE) != 0;
    var isRecallOnAccess = (fileAttr & FILE_ATTRIBUTES.RECALL_ON_DATA_ACCESS) != 0;
    actionsList.Add(new(file, fileName, opts, isOffline || isRecallOnAccess));
}

foreach (var key in fileOptions.Keys)
{
    var opts = fileOptions[key];
    if (opts.Target == null)
        continue;
    actionsList.Add(new(opts.Target, key, opts, false));
}

actionsList.Sort(
        (a1, a2) => FileNameComparer.Default.Compare(
            a1.FileName,
            a2.FileName)
    );

//
// Third step: 
//
Console.WriteLine("Processing files:");
foreach (var action in Helpers.LogProgress(actionsList, a => a.FileName))
{
    if (action.IsNotReady)
        continue;

    var currentPath = action.FullPath;
    if ((action.Options.Action & FileAction.InternetLink) != 0)
    {
        if (Regex.IsMatch(currentPath, @"^https?:"))
        {
            Bitmap bm = null;
            var favicon = Helpers.GetFavIconName(currentPath);
            var url = currentPath;
            var web = new HtmlWeb();
            var doc = web.Load(url);
            //doc.Save("xpto.html");
            foreach (var el in doc.DocumentNode.SelectNodes("//head/link[@rel='icon']"))
                favicon = Helpers.GetFavIconName(currentPath, el.GetAttributeValue("href", "/favicon.ico"));

            Directory.CreateDirectory(Path.Combine(shortcuts, "ShctIcons"));
            var iconFullPath = Path.Combine(shortcuts, "ShctIcons", action.FileName) + ".ico";
            var faviconBuffer = Helpers.Download(favicon);
            try
            {
                bm = new Bitmap(System.Drawing.Image.FromStream(new MemoryStream(faviconBuffer)));
            }
            catch { }
            if (bm != null)
            {
                Icon ic = Icon.FromHandle(bm.GetHicon());
                using (var icStream = File.Create(iconFullPath))
                    ic.Save(icStream);
            }

            void CreateInternetLink(string name)
            {
                var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                if (File.Exists(iconFullPath))
                {
                    Helpers.CreateShortcut(altFullPathLnk, currentPath, iconFullPath);
                    //using (var urlStream = File.Open(altFullPathLnk, FileMode.Append))
                    //using (var urlWriter = new StreamWriter(urlStream))
                    //    urlWriter.WriteLine($"IconFile={iconFullPath}");
                }
                else
                {
                    log.WriteLine($"Missing icon: {iconFullPath}");
                    Helpers.CreateShortcut(altFullPathLnk, currentPath);
                }
            }
            CreateInternetLink(action.FileName);
            foreach (var altname in action.Options.AltNames)
            {
                CreateInternetLink(altname);
            }
        }
    }

    if ((action.Options.Action & FileAction.FolderLink) != 0)
        {
        if (Directory.Exists(currentPath))
        {
            void CreateDirLinks(string name)
            {
                var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                Helpers.CreateShortcut(altFullPathLnk, currentPath);
                var driveLetter = Path.GetPathRoot(currentPath).Split(":")[0];

                var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                File.WriteAllText(altFullPathCmd, $"""
                    @echo off
                    cd "{currentPath}"
                    {driveLetter}:
                    """);

                var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                File.WriteAllText(altFullPathPs1, $"""
                    set-location "{currentPath}"
                    """);

                var altFullPathSh = Path.Combine(shortcuts, name) + ".sh";
                var unixPath = "/" + string.Join("/", currentPath.Split("\\").Skip(1));
                File.WriteAllText(altFullPathSh, $$"""
                    #!/usr/bin/env bash
                    function add_alias {
                        echo "Adding alias for $1 $2$3"
                        [ -d "/mnt/$2" ] && base="/mnt/$2" || base="/$2"
                        line="alias $1='cd $base$3'"
                        echo "    $line"
                        touch ~/.bashrc
                        if grep "$line" ~/.bashrc > /dev/null 2>&1
                        then
                            echo "Alias already present"
                        else
                            echo "$line" >> ~/.bashrc
                            echo "Alias added, entering subshell"
                            $SHELL
                        fi
                    }

                    add_alias "{{name}}" "{{driveLetter.ToLower()}}" "{{unixPath}}"

                    """.Replace("\r\n", "\n"));
            }
            var dirName = Path.GetFileName(currentPath);
            if (!action.Options.AltNames.Contains(dirName, StringComparer.InvariantCultureIgnoreCase)
                && !action.Options.ElevNames.Contains(dirName, StringComparer.InvariantCultureIgnoreCase))
            {
                CreateDirLinks(dirName);
            }
            foreach (var altname in action.Options.AltNames)
            {
                CreateDirLinks(altname);
            }
        }
    }

    if ((action.Options.Action & FileAction.MakeShortcut) != 0)
    {
        if (File.Exists(currentPath) && Helpers.HasExt(currentPath, ".lnk", ".url"))
        {
            var fileNameExt = Path.GetFileName(currentPath);
            var targetFullPath = Path.Combine(shortcuts, fileNameExt);
            Helpers.Copy(currentPath, targetFullPath);
            action.FullPath = targetFullPath;

            var ext = Path.GetExtension(currentPath);
            foreach (var altname in action.Options.AltNames)
            {
                var altFullPath = Path.Combine(shortcuts, altname + ext);
                Helpers.Copy(currentPath, altFullPath);
            }
            if (Helpers.HasExt(currentPath, ".lnk"))
            {
                foreach (var altname in action.Options.ElevNames)
                {
                    var altFullPath = Path.Combine(shortcuts, altname + ext);
                    Helpers.Copy(currentPath, altFullPath);
                    Helpers.MakeElevatedLink(altFullPath);
                }
            }
        }
    }

    if ((action.Options.Action & FileAction.DeleteDesktopLink) != 0)
    {
        var fileNameExt = Path.GetFileName(action.FullPath);
        var pathAtDesktop = Path.Combine(desktopPath, fileNameExt);
        try
        {
            if (Helpers.HasExt(currentPath, ".lnk", ".url"))
                if (File.Exists(pathAtDesktop))
                        File.Delete(pathAtDesktop);
        }
        catch (FileNotFoundException)
        {
        }

        var pathAtDesktop2 = Path.Combine(desktopPath2, fileNameExt);
        try
        {
            if (Helpers.HasExt(currentPath, ".lnk", ".url"))
                if (File.Exists(pathAtDesktop2))
                        File.Delete(pathAtDesktop2);
        }
        catch (FileNotFoundException)
        {
        }
    }
}

log.Close();