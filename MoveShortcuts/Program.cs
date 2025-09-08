// See https://aka.ms/new-console-template for more information
using HtmlAgilityPack;
using MoveShortcuts;
using Newtonsoft.Json;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Policy;
using System.Text.RegularExpressions;
using static System.Net.Mime.MediaTypeNames;

Console.WriteLine("Workdir: " + Directory.GetCurrentDirectory());
Console.WriteLine("Executable: " + System.Reflection.Assembly.GetExecutingAssembly().Location);

var log = new StreamWriter(File.Open("move-shortcuts.log", FileMode.Append));

var options = new Settings();

var optsFileName = "move-shortcuts-options.json";
if (File.Exists(optsFileName))
{
    Console.WriteLine("Found data file: " + optsFileName);
    var json = File.ReadAllText(optsFileName);
    options = JsonConvert.DeserializeObject<Settings>(json);
}
else
{
    Console.WriteLine("New data file: " + optsFileName);
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


// 2.1 - source files that have a match in the configurations file:
//      Can be either:
//      - a direct match: the filename without extension is in the config file
//      - a regex match: the filename without extension is matched by a config key representing a regex
// Note: the options matched here, must have the Target property set to null.
//       The targets are the links with the same name as the option by default.
var fileOptionsWithoutTarget = fileOptions
            .Where(kv => kv.Value.Target == null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);
foreach (var file in Helpers.LogProgress(allSourceFiles, Path.GetFileName))
{
    var fileName = Path.GetFileNameWithoutExtension(file);
    if (usedFilesNames.Contains(fileName))
        continue;
    usedFilesNames.Add(fileName);
    if (!fileOptionsWithoutTarget.TryGetValue(fileName, out MyFileOptions? opts))
    {
        var keyMatch = fileOptionsWithoutTarget.Keys
            .Where(x => !Path.IsPathFullyQualified(x))
            .FirstOrDefault(
                k => Regex.IsMatch(fileName, "^" + k + "$", RegexOptions.IgnoreCase));
        if (keyMatch != null)
            opts = fileOptionsWithoutTarget[keyMatch];
    }
    var fileAttr = (FILE_ATTRIBUTES)File.GetAttributes(file);
    var isOffline = (fileAttr & FILE_ATTRIBUTES.OFFLINE) != 0;
    var isRecallOnAccess = (fileAttr & FILE_ATTRIBUTES.RECALL_ON_DATA_ACCESS) != 0;
    if (opts != null)
    {
        opts.Action = opts.Action & (~FileAction.FileLink) & (~FileAction.FolderLink) & (~FileAction.InternetLink);
        actionsList.Add(new(file, fileName, opts, isOffline || isRecallOnAccess));
    }
}

// 2.2 - options that represent a full path for a file/folder that actually exists
// Deprectated: this option is depracated in favor of using fully qualified Target property.
//              See section 2.3
foreach (var file in fileOptionsWithoutTarget.Keys.Where(Path.IsPathFullyQualified))
{
    if (!File.Exists(file) && !Directory.Exists(file))
        continue;
    var fileName = Path.GetFileNameWithoutExtension(file);
    var opts = fileOptionsWithoutTarget[file];
    opts.Action = opts.Action & (FileAction.FileLink | FileAction.FolderLink);
    var fileAttr = (FILE_ATTRIBUTES)File.GetAttributes(file);
    var isOffline = (fileAttr & FILE_ATTRIBUTES.OFFLINE) != 0;
    var isRecallOnAccess = (fileAttr & FILE_ATTRIBUTES.RECALL_ON_DATA_ACCESS) != 0;
    actionsList.Add(new(file, fileName, opts, isOffline || isRecallOnAccess));
}

// If there is an option not matching files found in the Desktop, Start Menu or UWP Apps,
// then the option can be a standalone option, meaning that it can represent links or items
// that must be created dynamicaly. The following sections are for these types of options.
// Note: all of these options have the Target property set to something.
//       The Target propery indicates what type of items is this.
//       - URL: when Target starts with http:// or https://
//       - Path: when Target refers to an absolute Path, i.e. it starts with something like C:/
//       - Everything: Target refers to items found by using Everythin software, i.e. es filename.exe
//       - Where: Target refers to items found by using the "where" command, i.e. where filename
// Note: when using Everything or where command, the first item that is listed is used as the target

var fileOptionsWithTarget = fileOptions
            .Where(kv => kv.Value.Target != null)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

// 2.3 - options that have a Target property
foreach (var key in fileOptionsWithTarget.Keys)
{
    var opts = fileOptionsWithTarget[key];
    if (opts.Target.StartsWith("http://") || opts.Target.StartsWith("https://"))
    {
    opts.Action = opts.Action & (FileAction.InternetLink);
        actionsList.Add(new(opts.Target, key, opts, false));
    }
    else if (Path.IsPathFullyQualified(opts.Target))
    {
        opts.Action = opts.Action & (FileAction.FileLink | FileAction.FolderLink);
        actionsList.Add(new(opts.Target, key, opts, false));
    }
    else
    {
        opts.Action = opts.Action & (FileAction.FileLink | FileAction.FolderLink);
        var cmd = opts.Target.Split(" ", 2);
        var target = Helpers.RunCommandAndGetFirstLine(cmd[0], cmd[1]);
        if (string.IsNullOrWhiteSpace(target))
            continue;
        actionsList.Add(new(target, key, opts, false));
    }
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

    var targetObjectToOpen = action.FullPath;
    if ((action.Options.Action & FileAction.InternetLink) != 0)
    {
        if (Regex.IsMatch(targetObjectToOpen, @"^https?:"))
        {
            string iconFullPath = null;
            if (options.getFavIcon == true)
            {
                Bitmap? bm = null;
                var favicon = Helpers.GetFavIconName(targetObjectToOpen);
                var url = targetObjectToOpen;
                var web = new HtmlWeb();
                var doc = web.Load(url);
                //doc.Save("xpto.html");
                var head_link_icon = doc.DocumentNode.SelectNodes("//head/link[@rel='icon']");
                if (head_link_icon != null)
                    foreach (var el in head_link_icon)
                        favicon = Helpers.GetFavIconName(targetObjectToOpen, el.GetAttributeValue("href", "/favicon.ico"));
                else
                    favicon = Helpers.GetFavIconName(targetObjectToOpen, "/favicon.ico");

                Directory.CreateDirectory(Path.Combine(shortcuts, "ShctIcons"));
                iconFullPath = Path.Combine(shortcuts, "ShctIcons", action.FileName) + ".ico";
                var faviconBuffer = Helpers.Download(favicon);
                if (faviconBuffer != null)
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
            }

            void CreateInternetLink(string name)
            {
                var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                if (iconFullPath != null && File.Exists(iconFullPath))
                {
                    Helpers.CreateShortcut(altFullPathLnk, targetObjectToOpen, iconFullPath);
                    MakeGroups(shortcuts, action, altFullPathLnk);
                    //using (var urlStream = File.Open(altFullPathLnk, FileMode.Append))
                    //using (var urlWriter = new StreamWriter(urlStream))
                    //    urlWriter.WriteLine($"IconFile={iconFullPath}");
                }
                else
                {
                    if (options.getFavIcon == true)
                        log.WriteLine($"Missing icon: {iconFullPath}");
                    Helpers.CreateShortcut(altFullPathLnk, targetObjectToOpen);
                    MakeGroups(shortcuts, action, altFullPathLnk);
                }
            }
            CreateInternetLink(action.FileName);
            foreach (var altname in action.Options.AltNames)
            {
                CreateInternetLink(altname);
            }
        }
    }

    if ((action.Options.Action & FileAction.FileLink) != 0)
    {
        if (File.Exists(targetObjectToOpen) && !Helpers.HasExt(targetObjectToOpen, ".lnk", ".url"))
        {
            void CreateLocalLink(string name, bool elevated)
            {
                var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                Helpers.CreateShortcut(altFullPathLnk, targetObjectToOpen);
                MakeGroups(shortcuts, action, altFullPathLnk);
                if (elevated)
                    Helpers.MakeElevatedLink(altFullPathLnk);
            }
            CreateLocalLink(action.FileName, false);
            foreach (var altname in action.Options.AltNames)
            {
                CreateLocalLink(altname, false);
            }
            foreach (var altname in action.Options.ElevNames)
            {
                CreateLocalLink(altname, true);
            }
        }
    }

    if ((action.Options.Action & FileAction.FolderLink) != 0)
    {
        if (Directory.Exists(targetObjectToOpen))
        {
            void CreateDirLinks(string name)
            {
                var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                Helpers.CreateShortcut(altFullPathLnk, targetObjectToOpen);
                MakeGroups(shortcuts, action, altFullPathLnk);

                var driveLetter = Path.GetPathRoot(targetObjectToOpen).Split(":")[0];

                var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                File.WriteAllText(altFullPathCmd, $"""
                    @echo off
                    cd "{targetObjectToOpen}"
                    {driveLetter}:
                    """);

                var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                File.WriteAllText(altFullPathPs1, $"""
                    set-location "{targetObjectToOpen}"
                    """);

                var altFullPathSh = Path.Combine(shortcuts, name) + ".sh";
                var unixPath = "/" + string.Join("/", targetObjectToOpen.Split("\\").Skip(1));
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
            var dirName = Path.GetFileName(targetObjectToOpen);
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
        if (File.Exists(targetObjectToOpen) && Helpers.HasExt(targetObjectToOpen, ".lnk", ".url"))
        {
            var fileNameExt = Path.GetFileName(targetObjectToOpen);
            var targetFullPath = Path.Combine(shortcuts, fileNameExt);
            Helpers.Copy(targetObjectToOpen, targetFullPath);
            MakeGroups(shortcuts, action, targetFullPath);
            action.FullPath = targetFullPath;

            var ext = Path.GetExtension(targetObjectToOpen);
            foreach (var altname in action.Options.AltNames)
            {
                var altFullPath = Path.Combine(shortcuts, altname + ext);
                Helpers.Copy(targetObjectToOpen, altFullPath);
                //MakeGroups(shortcuts, action, altFullPath);
            }
            if (Helpers.HasExt(targetObjectToOpen, ".lnk"))
            {
                foreach (var altname in action.Options.ElevNames)
                {
                    var altFullPath = Path.Combine(shortcuts, altname + ext);
                    Helpers.Copy(targetObjectToOpen, altFullPath);
                    Helpers.MakeElevatedLink(altFullPath);
                }
                if (action.Options.ElevNames.Count > 0)
                {
                    var toCreateElev = Path.Combine(shortcuts, action.FileName + " Elevated" + ext);
                    Helpers.Copy(targetObjectToOpen, toCreateElev);
                    Helpers.MakeElevatedLink(toCreateElev);
                    MakeGroups(shortcuts, action, toCreateElev);
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
            if (Helpers.HasExt(targetObjectToOpen, ".lnk", ".url"))
                if (File.Exists(pathAtDesktop))
                        File.Delete(pathAtDesktop);
        }
        catch (FileNotFoundException)
        {
        }

        var pathAtDesktop2 = Path.Combine(desktopPath2, fileNameExt);
        try
        {
            if (Helpers.HasExt(targetObjectToOpen, ".lnk", ".url"))
                if (File.Exists(pathAtDesktop2))
                        File.Delete(pathAtDesktop2);
        }
        catch (FileNotFoundException)
        {
        }
    }
}

log.Close();

static void MakeGroups(string shortcuts, MyFileAction action, string altFullPathLnk)
{
    foreach (var grp in action.Options.Groups)
    {
        var copyTo = Path.Combine(shortcuts, grp, Path.GetFileName(altFullPathLnk));
        Directory.CreateDirectory(Path.Combine(shortcuts, grp));
        Helpers.Copy(altFullPathLnk, copyTo);
    }
}