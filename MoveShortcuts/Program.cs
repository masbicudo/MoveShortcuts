// SPDX-License-Identifier: Apache-2.0

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

if (IsHelpRequested(args))
{
    PrintHelp();
    return;
}

var progressOverride = GetProgressOverride(args);
var refreshUwpCache = IsRefreshUwpCacheRequested(args);
var optsFileName = "move-shortcuts-options.json";

if (IsInitRequested(args))
{
    RunInit(optsFileName, progressOverride);
    return;
}

var options = new Settings();
var foundOptionsFile = File.Exists(optsFileName);
if (foundOptionsFile)
{
    var json = File.ReadAllText(optsFileName);
    options = JsonConvert.DeserializeObject<Settings>(json) ?? new Settings();
    InitOptionsBuilder.NormalizeSettings(options);
}
else
{
    Console.WriteLine("No data file found: " + optsFileName);
    Console.WriteLine("Run MoveShortcuts init to create one.");
    return;
}

Helpers.SetProgressMode(Helpers.ResolveProgressMode(progressOverride ?? options.progress));
Helpers.WriteLine("Workdir: " + Directory.GetCurrentDirectory());
Helpers.WriteLine("Executable: " + System.Reflection.Assembly.GetExecutingAssembly().Location);
Helpers.WriteLine((foundOptionsFile ? "Found" : "New") + " data file: " + optsFileName);

var log = new StreamWriter(File.Open("move-shortcuts.log", FileMode.Append));

var fileOptions = options.fileOptions;
InitOptionsBuilder.ApplyGlobalCleanupSetting(options);
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
var uwpShortcutsFolder = Path.Combine(shortcuts, "UWP Apps");
if (options.sources.uwpApps && Helpers.HasUwpShortcutOptions(fileOptions))
{
    Helpers.WriteLine("Creating shortcuts for UWP programs:");
    var uwpApps = UwpAppCacheProvider.GetUwpApps(
        UwpAppCacheProvider.DefaultCacheFileName,
        refreshUwpCache,
        Helpers.GetUwpApps,
        message => Helpers.WriteLine(message));
    Helpers.CreateUWPShortcuts(uwpShortcutsFolder, fileOptions, uwpApps);
}

Helpers.WriteLine("Collecting items locations");
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

    if (options.sources.desktop)
    {
        var allDesktopFiles = Directory.GetFiles(desktopPath, "*.*", dirEnumOpts).ToList();
        allDesktopFiles.Sort(revFileNameComparer);
        allSourceFiles.AddRange(allDesktopFiles);

        var allDesktopFiles2 = Directory.GetFiles(desktopPath2, "*.*", dirEnumOpts).ToList();
        allDesktopFiles2.Sort(revFileNameComparer);
        allSourceFiles.AddRange(allDesktopFiles2);
    }

    if (options.sources.startMenu)
    {
        var allStartMenuFiles = Directory.GetFiles(startMenuPath, "*.*", dirEnumOptsRecursive).ToList();
        allStartMenuFiles.Sort(revFileNameComparer);
        allSourceFiles.AddRange(allStartMenuFiles);

        var allStartMenuFiles2 = Directory.GetFiles(startMenuPath2, "*.*", dirEnumOptsRecursive).ToList();
        allStartMenuFiles2.Sort(revFileNameComparer);
        allSourceFiles.AddRange(allStartMenuFiles2);
    }

    if (options.sources.uwpApps && Directory.Exists(uwpShortcutsFolder))
    {
        var allUwpApps = Directory.GetFiles(uwpShortcutsFolder, "*.*", dirEnumOptsRecursive).ToList();
        allUwpApps.Sort(revFileNameComparer);
        allSourceFiles.AddRange(allUwpApps);
    }

}
Helpers.WriteLine($"  Found {allSourceFiles.Count} items");

//
// Second step: matching collected files with items from the settings
//
Helpers.WriteLine("Collecting files to process:");
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
var fileOptionMatcher = new FileOptionMatcher(fileOptionsWithoutTarget, includeFullyQualifiedKeys: false);
foreach (var file in Helpers.LogProgress(allSourceFiles, Path.GetFileName))
{
    var fileName = Path.GetFileNameWithoutExtension(file);
    if (usedFilesNames.Contains(fileName))
        continue;
    usedFilesNames.Add(fileName);
    fileOptionMatcher.TryGetOptions(fileName, out MyFileOptions? opts);
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
FileAction ActionsForResolvedTarget(string target)
{
    if (File.Exists(target) && Helpers.HasExt(target, ".lnk", ".url"))
        return FileAction.MakeShortcut;

    return FileAction.FileLink | FileAction.FolderLink;
}

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
        opts.Action = opts.Action & ActionsForResolvedTarget(opts.Target);
        actionsList.Add(new(opts.Target, key, opts, false));
    }
    else
    {
        var cmd = opts.Target.Split(" ", 2);
        var target = Helpers.RunCommandAndGetFirstLine(cmd[0], cmd[1]);
        if (string.IsNullOrWhiteSpace(target))
            continue;
        opts.Action = opts.Action & ActionsForResolvedTarget(target);
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
Helpers.WriteLine("Processing files:");
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
                    if (Helpers.CreateShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, iconFullPath))
                        MakeGroups(shortcuts, action, altFullPathLnk);
                    //using (var urlStream = File.Open(altFullPathLnk, FileMode.Append))
                    //using (var urlWriter = new StreamWriter(urlStream))
                    //    urlWriter.WriteLine($"IconFile={iconFullPath}");
                }
                else
                {
                    if (options.getFavIcon == true)
                        log.WriteLine($"Missing icon: {iconFullPath}");
                    if (Helpers.CreateShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen))
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
                string workdir = null;
                if (action.Options.WorkDir != null)
                    workdir = action.Options.WorkDir.Replace("$TargetDir", Path.GetDirectoryName(targetObjectToOpen));
                var linTypes = action.Options.LinkTypes;
                if (linTypes.Count == 0)
                    linTypes = new() { "lnk" };
                if (linTypes.Contains("lnk", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathLnk = Path.Combine(shortcuts, name) + ".lnk";
                    var created = Helpers.CreateShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, workdir: workdir);
                    if (created)
                        MakeGroups(shortcuts, action, altFullPathLnk);
                    if (created && elevated)
                        Helpers.MakeElevatedLink(altFullPathLnk);
                }
                if (linTypes.Contains("ps1", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathPs1))
                        Helpers.CreatePowerShellProxy(altFullPathPs1, targetObjectToOpen, elevated, workdir);
                }
                if (linTypes.Contains("cmd", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathCmd))
                        Helpers.CreateCommandPromptProxy(altFullPathCmd, targetObjectToOpen, elevated, workdir);
                }
                if (linTypes.Contains("sh", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathSh = Path.Combine(shortcuts, name) + ".sh";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathSh))
                        Helpers.CreateGitBashProxy(altFullPathSh, targetObjectToOpen, elevated, workdir);
                }
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
                if (Helpers.CreateShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen))
                    MakeGroups(shortcuts, action, altFullPathLnk);

                var driveLetter = Path.GetPathRoot(targetObjectToOpen).Split(":")[0];

                var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                Helpers.WriteShortcutOutput(shortcuts, altFullPathCmd, $"""
                    @echo off
                    cd "{targetObjectToOpen}"
                    {driveLetter}:
                    """);

                var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                Helpers.WriteShortcutOutput(shortcuts, altFullPathPs1, $"""
                    set-location "{targetObjectToOpen}"
                    """);

                var altFullPathSh = Path.Combine(shortcuts, name) + ".sh";
                var unixPath = "/" + string.Join("/", targetObjectToOpen.Split("\\").Skip(1));
                Helpers.WriteShortcutOutput(shortcuts, altFullPathSh, $$"""
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
            var ext = Path.GetExtension(targetObjectToOpen);
            var fileNameExt = action.FileName + ext;
            var targetFullPath = Path.Combine(shortcuts, fileNameExt);
            if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, targetFullPath))
            {
                MakeGroups(shortcuts, action, targetFullPath);
                action.FullPath = targetFullPath;
            }

            foreach (var altname in action.Options.AltNames)
            {
                var altFullPath = Path.Combine(shortcuts, altname + ext);
                if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, altFullPath))
                    MakeGroups(shortcuts, action, altFullPath);
            }
            if (Helpers.HasExt(targetObjectToOpen, ".lnk"))
            {
                foreach (var altname in action.Options.ElevNames)
                {
                    var altFullPath = Path.Combine(shortcuts, altname + ext);
                    if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, altFullPath))
                        Helpers.MakeElevatedLink(altFullPath);
                }
                if (action.Options.ElevNames.Count > 0)
                {
                    var toCreateElev = Path.Combine(shortcuts, action.FileName + " Elevated" + ext);
                    if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, toCreateElev))
                    {
                        Helpers.MakeElevatedLink(toCreateElev);
                        MakeGroups(shortcuts, action, toCreateElev);
                    }
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

static string? GetProgressOverride(string[] args)
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))
            return "quiet";
        if (arg.Equals("--log", StringComparison.OrdinalIgnoreCase))
            return "log";
        if (arg.Equals("--cli", StringComparison.OrdinalIgnoreCase))
            return "cli";
        if (arg.Equals("--progress", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 >= args.Length)
                throw new ArgumentException("--progress requires quiet, log, or cli.");
            return args[++i];
        }
        if (arg.StartsWith("--progress=", StringComparison.OrdinalIgnoreCase))
            return arg.Substring("--progress=".Length);
    }

    return null;
}

static bool IsHelpRequested(string[] args)
{
    return args.Any(arg =>
        arg.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("-help", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("/?", StringComparison.OrdinalIgnoreCase));
}

static bool IsInitRequested(string[] args)
{
    return args.Any(arg => arg.Equals("init", StringComparison.OrdinalIgnoreCase));
}

static bool IsRefreshUwpCacheRequested(string[] args)
{
    return args.Any(arg => arg.Equals("--refresh-uwp-cache", StringComparison.OrdinalIgnoreCase));
}

static void RunInit(string optsFileName, string? progressOverride)
{
    Helpers.SetProgressMode(Helpers.ResolveProgressMode(progressOverride ?? "log"));

    var outputPath = Path.GetFullPath(optsFileName);
    Helpers.WriteLine("MoveShortcuts init");
    Helpers.WriteLine("");

    var options = new Settings
    {
        shortcuts = AskString("Shortcut directory", @"C:\Shortcuts"),
        progress = "auto",
        sources = new SourceSettings
        {
            desktop = AskYesNo("Scan Desktop shortcuts?", defaultValue: true),
            startMenu = AskYesNo("Scan Start Menu shortcuts?", defaultValue: true),
            uwpApps = AskYesNo("Scan UWP / AppsFolder programs?", defaultValue: true),
        },
        cleanup = new CleanupSettings(),
        aliases = new AliasSettings(),
        fileOptions = new Dictionary<string, MyFileOptions>(),
    };

    options.cleanup.deleteDesktopShortcuts = options.sources.desktop
        && AskYesNo(
            "MoveShortcuts can unclutter your desktop from installer icons. Remove matching desktop shortcuts after managed shortcuts are created?",
            defaultValue: false);

    options.aliases.generateInitials = AskYesNo(
        "Generate short aliases from words and capital letters? Examples: Visual Studio Code -> vsc, FireFox -> ff.",
        defaultValue: true);

    if (options.aliases.generateInitials)
        options.aliases.minimumLength = AskInt("Minimum generated alias length", defaultValue: 2, minimum: 1);

    Helpers.WriteLine("");
    Helpers.WriteLine("Scanning selected sources...");
    var initScan = ScanForInit(options);
    options.fileOptions = initScan.FileOptions;

    Helpers.WriteLine("");
    Helpers.WriteLine("Your choices will affect:");
    if (options.sources.desktop)
    {
        var operation = options.cleanup.deleteDesktopShortcuts ? "moved to" : "copied to";
        Helpers.WriteLine($"- {initScan.DesktopShortcutCount} desktop icons, and they will be {operation} the Shortcuts folder");
    }
    else
    {
        Helpers.WriteLine("- 0 desktop icons; Desktop scanning is disabled");
    }

    Helpers.WriteLine(options.sources.startMenu
        ? $"- {initScan.StartMenuShortcutCount} start menu icons will be copied"
        : "- 0 start menu icons; Start Menu scanning is disabled");

    Helpers.WriteLine(options.sources.uwpApps
        ? $"- {initScan.UwpProgramCount} UWP programs will have shortcuts added"
        : "- 0 UWP programs; UWP scanning is disabled");

    if (options.aliases.generateInitials)
    {
        Helpers.WriteLine($"- {initScan.GeneratedAliasCount} generated aliases will be added");
        if (initScan.SkippedAliasCount > 0)
            Helpers.WriteLine($"- {initScan.SkippedAliasCount} generated aliases were skipped because they conflict with existing commands or aliases");
    }

    Helpers.WriteLine("");
    Helpers.WriteLine($"This will just create the options file at {outputPath}");
    Helpers.WriteLine("You can revise it before doing anything for real.");
    if (File.Exists(outputPath))
        Helpers.WriteLine("The file already exists and will be overwritten if you proceed.");

    if (!AskYesNo("Would you like to proceed?", defaultValue: false))
    {
        Helpers.WriteLine("Init cancelled. No files were changed.");
        return;
    }

    var json = JsonConvert.SerializeObject(options, Formatting.Indented);
    File.WriteAllText(outputPath, json);
    Helpers.WriteLine($"Created options file: {outputPath}");
}

static InitScanResult ScanForInit(Settings options)
{
    var dirEnumOptsRecursive = new EnumerationOptions
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = true,
    };
    var dirEnumOpts = new EnumerationOptions
    {
        IgnoreInaccessible = true,
    };
    var shortcuts = options.shortcuts;
    var entries = new Dictionary<string, InitSourceEntry>(StringComparer.OrdinalIgnoreCase);
    var desktopCount = 0;
    var startMenuCount = 0;
    var uwpCount = 0;

    if (options.sources.desktop)
    {
        var desktopFiles = GetFilesIfDirectoryExists(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "*.*", dirEnumOpts)
            .Concat(GetFilesIfDirectoryExists(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), "*.*", dirEnumOpts))
            .Where(IsShortcutLike)
            .ToList();
        desktopCount = desktopFiles.Count;
        foreach (var file in desktopFiles)
            AddInitEntry(entries, file, SourceKind.Desktop);
    }

    if (options.sources.startMenu)
    {
        var startMenuFiles = GetFilesIfDirectoryExists(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "*.*", dirEnumOptsRecursive)
            .Concat(GetFilesIfDirectoryExists(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "*.*", dirEnumOptsRecursive))
            .Where(IsShortcutLike)
            .ToList();
        startMenuCount = startMenuFiles.Count;
        foreach (var file in startMenuFiles)
            AddInitEntry(entries, file, SourceKind.StartMenu);
    }

    if (options.sources.uwpApps)
    {
        var uwpApps = Helpers.GetUwpApps();
        uwpCount = uwpApps.Count;
        foreach (var name in uwpApps.Keys)
            AddInitEntry(entries, name, SourceKind.Uwp);
    }

    var aliasSet = new HashSet<string>(entries.Keys, StringComparer.OrdinalIgnoreCase);
    var generatedAliasCount = 0;
    var skippedAliasCount = 0;
    foreach (var entry in entries.Values)
    {
        var option = InitOptionsBuilder.CreateFileOptions(options.cleanup.deleteDesktopShortcuts);
        if (options.aliases.generateInitials)
        {
            var aliasResult = InitOptionsBuilder.TryAddInitialsAlias(
                option,
                entry.Name,
                aliasSet,
                shortcuts,
                options.aliases.minimumLength);
            if (aliasResult.Status == InitAliasStatus.Added)
            {
                generatedAliasCount++;
            }
            else if (aliasResult.Status == InitAliasStatus.Skipped)
            {
                skippedAliasCount++;
            }
        }

        entry.FileOptions = option;
    }

    return new InitScanResult(
        entries.ToDictionary(kv => kv.Key, kv => kv.Value.FileOptions, StringComparer.OrdinalIgnoreCase),
        desktopCount,
        startMenuCount,
        uwpCount,
        generatedAliasCount,
        skippedAliasCount);
}

static IEnumerable<string> GetFilesIfDirectoryExists(string directory, string searchPattern, EnumerationOptions options)
{
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        return Enumerable.Empty<string>();

    return Directory.GetFiles(directory, searchPattern, options);
}

static bool IsShortcutLike(string path)
{
    return Helpers.HasExt(path, ".lnk", ".url");
}

static void AddInitEntry(Dictionary<string, InitSourceEntry> entries, string fileOrName, SourceKind sourceKind)
{
    var name = sourceKind == SourceKind.Uwp
        ? fileOrName
        : Path.GetFileNameWithoutExtension(fileOrName);

    if (string.IsNullOrWhiteSpace(name))
        return;

    if (!entries.ContainsKey(name))
        entries[name] = new InitSourceEntry(name, sourceKind);
}

static bool AskYesNo(string question, bool defaultValue)
{
    var suffix = defaultValue ? " [Y/n] " : " [y/N] ";
    while (true)
    {
        Console.Write(question + suffix);
        var answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return defaultValue;
        if (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
            return true;
        if (answer.Equals("n", StringComparison.OrdinalIgnoreCase) || answer.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;
        Console.WriteLine("Please answer yes or no.");
    }
}

static string AskString(string question, string defaultValue)
{
    Console.Write($"{question} [{defaultValue}] ");
    var answer = Console.ReadLine()?.Trim();
    return string.IsNullOrWhiteSpace(answer) ? defaultValue : answer;
}

static int AskInt(string question, int defaultValue, int minimum)
{
    while (true)
    {
        Console.Write($"{question} [{defaultValue}] ");
        var answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(answer))
            return defaultValue;
        if (int.TryParse(answer, out var value) && value >= minimum)
            return value;
        Console.WriteLine($"Please enter a number greater than or equal to {minimum}.");
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
        MoveShortcuts

        Usage:
          MoveShortcuts [options]
          MoveShortcuts init [options]

        Options:
          -h, -help, --help, /?
              Show this help text and exit.

          --progress <mode>
              Select progress output. Modes: quiet, log, cli.

          --quiet
              Hide progress counters.

          --log
              Print each progress item on its own line.

          --cli
              Use compact single-line terminal progress.

          --refresh-uwp-cache
              Ignore any existing UWP app cache and rebuild it from AppsFolder.

        Configuration:
          Reads move-shortcuts-options.json from the current working directory.
          The config file can also set "progress": "auto", "quiet", "log", or "cli".

        Commands:
          init
              Ask a few questions, scan selected sources, estimate the effect,
              and create move-shortcuts-options.json without creating shortcuts.

        Default progress:
          auto: cli in an interactive terminal, log when output is redirected.
        """);
}

record InitScanResult(
    Dictionary<string, MyFileOptions> FileOptions,
    int DesktopShortcutCount,
    int StartMenuShortcutCount,
    int UwpProgramCount,
    int GeneratedAliasCount,
    int SkippedAliasCount);

class InitSourceEntry
{
    public InitSourceEntry(string name, SourceKind sourceKind)
    {
        Name = name;
        SourceKind = sourceKind;
    }

    public string Name { get; }
    public SourceKind SourceKind { get; }
    public MyFileOptions FileOptions { get; set; } = new();
}

enum SourceKind
{
    Desktop,
    StartMenu,
    Uwp
}
