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
var userPathRequest = GetPathPlacementRequest(args, "--add-user-path");
var machinePathRequest = GetPathPlacementRequest(args, "--add-machine-path");
var optsFileName = "move-shortcuts-options.json";

if (IsEditRequested(args))
{
    RunEdit(optsFileName);
    return;
}

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

var fileOptions = options.fileOptions;
InitOptionsBuilder.ApplyGlobalCleanupSetting(options);
var shortcuts = options.shortcuts;

// ensuring shortcuts directory is there
Directory.CreateDirectory(shortcuts);
var outputManifest = OwnedOutputManifest.Load(shortcuts, "move-shortcuts-manifest.json", "MoveShortcuts");
var pathOnlyRequest = IsPathOnlyRequest(args, userPathRequest, machinePathRequest);
ApplyPathSettings(options, shortcuts, userPathRequest, machinePathRequest);
if (pathOnlyRequest)
    return;

var autoIgnoreManifestConflicts = IsManifestMergeAutoResolve(args, "ignore");
var interactiveManifestMerge = IsManifestMergeInteractive(args);
outputManifest.AutoIgnoreConflicts = autoIgnoreManifestConflicts;
if (interactiveManifestMerge)
    outputManifest.ConflictResolver = ResolveOutputConflictInteractively;

if (IsStartupCommand(args))
{
    RunStartupCommand(args, options);
    return;
}

if (IsManifestCommand(args) && !autoIgnoreManifestConflicts && !interactiveManifestMerge)
{
    RunManifestCommand(args, options, outputManifest);
    return;
}

Helpers.WriteLine("Workdir: " + Directory.GetCurrentDirectory());
Helpers.WriteLine("Executable: " + System.Reflection.Assembly.GetExecutingAssembly().Location);
Helpers.WriteLine((foundOptionsFile ? "Found" : "New") + " data file: " + optsFileName);

var log = new StreamWriter(File.Open("move-shortcuts.log", FileMode.Append));

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
    Helpers.CreateUWPShortcuts(uwpShortcutsFolder, fileOptions, uwpApps, outputManifest);
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
        if (cmd.Length < 2)
            continue;

        var target = ResolvedTargetCacheProvider.Resolve(
            ResolvedTargetCacheProvider.DefaultCacheFileName,
            cmd[0],
            cmd[1],
            Helpers.RunCommandAndGetFirstLine,
            message => Helpers.WriteLine(message));
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
                    if (Helpers.CreateOwnedShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, iconFullPath, manifest: outputManifest))
                        MakeGroups(shortcuts, action, altFullPathLnk, outputManifest);
                    //using (var urlStream = File.Open(altFullPathLnk, FileMode.Append))
                    //using (var urlWriter = new StreamWriter(urlStream))
                    //    urlWriter.WriteLine($"IconFile={iconFullPath}");
                }
                else
                {
                    if (options.getFavIcon == true)
                        log.WriteLine($"Missing icon: {iconFullPath}");
                    if (Helpers.CreateOwnedShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, manifest: outputManifest))
                        MakeGroups(shortcuts, action, altFullPathLnk, outputManifest);
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
                    var created = Helpers.CreateOwnedShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, workdir: workdir, arguments: action.Options.Arguments, manifest: outputManifest);
                    if (created)
                        MakeGroups(shortcuts, action, altFullPathLnk, outputManifest);
                    if (created && elevated)
                        Helpers.MakeElevatedLink(altFullPathLnk);
                }
                if (linTypes.Contains("ps1", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathPs1) && outputManifest.CanWriteOrHandleConflict(altFullPathPs1))
                    {
                        Helpers.CreatePowerShellProxy(altFullPathPs1, targetObjectToOpen, elevated, workdir, action.Options.Arguments);
                        outputManifest.Touch(altFullPathPs1, targetObjectToOpen);
                    }
                }
                if (linTypes.Contains("cmd", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathCmd) && outputManifest.CanWriteOrHandleConflict(altFullPathCmd))
                    {
                        Helpers.CreateCommandPromptProxy(altFullPathCmd, targetObjectToOpen, elevated, workdir, action.Options.Arguments);
                        outputManifest.Touch(altFullPathCmd, targetObjectToOpen);
                    }
                }
                if (linTypes.Contains("sh", StringComparer.OrdinalIgnoreCase))
                {
                    var altFullPathSh = Path.Combine(shortcuts, name) + ".sh";
                    if (!Helpers.WouldShadowExternalCommand(shortcuts, altFullPathSh) && outputManifest.CanWriteOrHandleConflict(altFullPathSh))
                    {
                        Helpers.CreateGitBashProxy(altFullPathSh, targetObjectToOpen, elevated, workdir, action.Options.Arguments);
                        outputManifest.Touch(altFullPathSh, targetObjectToOpen);
                    }
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
                if (Helpers.CreateOwnedShortcutOutput(shortcuts, altFullPathLnk, targetObjectToOpen, manifest: outputManifest))
                    MakeGroups(shortcuts, action, altFullPathLnk, outputManifest);

                var driveLetter = Path.GetPathRoot(targetObjectToOpen).Split(":")[0];

                var altFullPathCmd = Path.Combine(shortcuts, name) + ".cmd";
                Helpers.WriteShortcutOutput(shortcuts, altFullPathCmd, $"""
                    @echo off
                    cd "{targetObjectToOpen}"
                    {driveLetter}:
                    """, outputManifest);

                var altFullPathPs1 = Path.Combine(shortcuts, name) + ".ps1";
                Helpers.WriteShortcutOutput(shortcuts, altFullPathPs1, $"""
                    set-location "{targetObjectToOpen}"
                    """, outputManifest);

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

                    """.Replace("\r\n", "\n"), outputManifest);
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
            if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, targetFullPath, outputManifest))
            {
                MakeGroups(shortcuts, action, targetFullPath, outputManifest);
                action.FullPath = targetFullPath;
            }

            foreach (var altname in action.Options.AltNames)
            {
                var altFullPath = Path.Combine(shortcuts, altname + ext);
                if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, altFullPath, outputManifest))
                    MakeGroups(shortcuts, action, altFullPath, outputManifest);
            }
            if (Helpers.HasExt(targetObjectToOpen, ".lnk"))
            {
                foreach (var altname in action.Options.ElevNames)
                {
                    var altFullPath = Path.Combine(shortcuts, altname + ext);
                    if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, altFullPath, outputManifest))
                        Helpers.MakeElevatedLink(altFullPath);
                }
                if (action.Options.ElevNames.Count > 0)
                {
                    var toCreateElev = Path.Combine(shortcuts, action.FileName + " Elevated" + ext);
                    if (Helpers.CopyShortcutOutput(shortcuts, targetObjectToOpen, toCreateElev, outputManifest))
                    {
                        Helpers.MakeElevatedLink(toCreateElev);
                        MakeGroups(shortcuts, action, toCreateElev, outputManifest);
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
BuildProgramStarterFolder(options, actionsList, outputManifest, autoIgnoreManifestConflicts, interactiveManifestMerge);
outputManifest.RemoveStaleTouchedScope(relativePath =>
    !relativePath.StartsWith(options.programStarter.folderName + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
outputManifest.Save();

static void MakeGroups(string shortcuts, MyFileAction action, string altFullPathLnk, OwnedOutputManifest outputManifest)
{
    foreach (var grp in action.Options.Groups)
    {
        var copyTo = Path.Combine(shortcuts, grp, Path.GetFileName(altFullPathLnk));
        Directory.CreateDirectory(Path.Combine(shortcuts, grp));
        if (!outputManifest.CanWriteOrHandleConflict(copyTo))
            continue;
        Helpers.Copy(altFullPathLnk, copyTo);
        outputManifest.Touch(copyTo, altFullPathLnk);
    }
}

static void BuildProgramStarterFolder(Settings options, List<MyFileAction> actionsList, OwnedOutputManifest rootManifest, bool autoIgnoreConflicts, bool interactiveMerge)
{
    if (!options.programStarter.enabled)
    {
        var disabledFolder = Path.Combine(options.shortcuts, options.programStarter.folderName);
        if (Directory.Exists(disabledFolder))
        {
            var disabledManifest = OwnedOutputManifest.Load(disabledFolder, "program-starter.json", "MoveShortcuts ProgramStarter");
            disabledManifest.RemoveStaleTouchedScope();
            disabledManifest.Save();
        }
        return;
    }

    var startupFolder = Path.Combine(options.shortcuts, options.programStarter.folderName);
    Directory.CreateDirectory(startupFolder);
    var startupManifest = OwnedOutputManifest.Load(startupFolder, "program-starter.json", "MoveShortcuts ProgramStarter");
    startupManifest.AutoIgnoreConflicts = autoIgnoreConflicts;
    startupManifest.File.ShortcutsRoot = options.shortcuts;

    foreach (var action in actionsList)
    {
        if (action.Options.Startup == null)
            continue;

        if (!TryFindManagedActionOutput(options.shortcuts, action.FileName, rootManifest, out var sourcePath))
        {
            Helpers.WriteLine($"Skipping startup entry for {action.FileName}: no owned shortcut output was created.");
            continue;
        }

        var delay = NormalizeStartupDelay(action.Options.Startup.Delay);
        var window = NormalizeStartupWindow(action.Options.Startup.Window);
        var extension = Path.GetExtension(sourcePath);
        var plan = StartupMergePlanner.Plan(startupFolder, startupManifest, action.FileName, extension, delay);
        if (plan.Status == StartupMergeStatus.Conflict)
        {
            if (startupManifest.IsIgnoredConflict(plan.Identity, plan.Fingerprint))
            {
                if (startupManifest.TryFindByIdentity(plan.Identity, out var ignoredRelativePath, out var ignoredEntry))
                    startupManifest.Touch(Path.Combine(startupFolder, ignoredRelativePath), ignoredEntry.Source, ignoredEntry.Identity);
                continue;
            }

            startupManifest.ClearIgnoredConflicts(plan.Identity);
            if (interactiveMerge && plan.Fingerprint != null)
            {
                var resolution = ResolveStartupConflictInteractively(action.FileName, startupFolder, startupManifest, plan);
                if (resolution == ManifestConflictResolution.Ignore)
                    startupManifest.IgnoreConflict(plan.Identity, plan.Fingerprint);
                if (resolution == ManifestConflictResolution.AcceptFile)
                    AcceptStartupFile(startupFolder, startupManifest, plan, window);
                if (resolution == ManifestConflictResolution.ForgetOwned)
                    startupManifest.RemoveByIdentity(plan.Identity);
                if (resolution == ManifestConflictResolution.UseOptions)
                {
                    if (!DeleteStartupConflictFiles(startupFolder, plan))
                        continue;
                    Helpers.Copy(sourcePath, plan.TargetPath);
                    var resolvedEntry = startupManifest.Touch(plan.TargetPath, sourcePath, plan.Identity);
                    resolvedEntry.Delay = delay;
                    resolvedEntry.Window = window;
                }
                if (startupManifest.TryFindByIdentity(plan.Identity, out var resolvedRelativePath, out var resolvedEntryForTouch))
                    startupManifest.Touch(Path.Combine(startupFolder, resolvedRelativePath), resolvedEntryForTouch.Source, resolvedEntryForTouch.Identity);
                continue;
            }

            if (autoIgnoreConflicts && plan.Fingerprint != null)
            {
                startupManifest.IgnoreConflict(plan.Identity, plan.Fingerprint);
                Helpers.WriteLine($"Ignoring startup conflict for {action.FileName}: {plan.Message}.");
            }
            else
            {
                Helpers.WriteLine($"Skipping startup entry for {action.FileName}: {plan.Message}.");
            }

            if (startupManifest.TryFindByIdentity(plan.Identity, out var conflictRelativePath, out var conflictEntry))
                startupManifest.Touch(Path.Combine(startupFolder, conflictRelativePath), conflictEntry.Source, conflictEntry.Identity);
            continue;
        }

        startupManifest.ClearIgnoredConflicts(plan.Identity);

        if (plan.OldOwnedPath != null
            && File.Exists(plan.OldOwnedPath)
            && !plan.OldOwnedPath.Equals(plan.TargetPath, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Delete(plan.OldOwnedPath);
            }
            catch (UnauthorizedAccessException ex)
            {
                Helpers.WriteLine($"Could not rename startup entry for {action.FileName}: access denied deleting old file ({ex.Message})");
                continue;
            }
            catch (IOException ex)
            {
                Helpers.WriteLine($"Could not rename startup entry for {action.FileName}: delete failed for old file ({ex.Message})");
                continue;
            }
        }

        if (!startupManifest.CanWrite(plan.TargetPath))
        {
            Helpers.WriteLine($"Skipping startup entry {Path.GetFileName(plan.TargetPath)}: file exists but is not owned by MoveShortcuts.");
            continue;
        }

        Helpers.Copy(sourcePath, plan.TargetPath);
        var entry = startupManifest.Touch(plan.TargetPath, sourcePath, plan.Identity);
        entry.Delay = delay;
        entry.Window = window;
    }

    startupManifest.RemoveStaleTouchedScope();
    startupManifest.Save();

    if (options.programStarter.installAtLogon)
        EnsureProgramStarterInstalled(options, askIfMissing: startupManifest.ManifestExisted);
}

static bool TryFindManagedActionOutput(string shortcuts, string name, OwnedOutputManifest rootManifest, out string path)
{
    foreach (var ext in new[] { ".lnk", ".url", ".cmd", ".ps1", ".sh" })
    {
        var candidate = Path.Combine(shortcuts, name + ext);
        var relativePath = Path.GetRelativePath(shortcuts, candidate);
        if (File.Exists(candidate) && rootManifest.TouchedRelativePaths.Contains(relativePath, StringComparer.OrdinalIgnoreCase))
        {
            path = candidate;
            return true;
        }
    }

    path = "";
    return false;
}

static bool TryGetStartupLogicalIdentity(string path, out string identity)
    => StartupMergePlanner.TryGetStartupLogicalIdentity(path, out identity);

static ManifestConflictResolution ResolveOutputConflictInteractively(ManifestConflict conflict)
{
    Helpers.WriteLine("");
    Helpers.WriteLine("Manifest conflict:");
    Helpers.WriteLine($"- File: {conflict.DisplayPath}");
    Helpers.WriteLine("- Manifest: [not present]");
    Helpers.WriteLine($"- Options: {conflict.Fingerprint.OptionsPath ?? conflict.DisplayPath}");
    Helpers.WriteLine("");
    Helpers.WriteLine("[1] skip        Leave unresolved and report again later.");
    Helpers.WriteLine("[2] ignore      Suppress this exact conflict until it changes.");
    Helpers.WriteLine("[3] include-user Add this file to the manifest, but do not overwrite it now.");
    Helpers.WriteLine("[4] use-options Add this file to the manifest and overwrite it with configured output.");
    return AskResolution(
        new Dictionary<string, ManifestConflictResolution>
        {
            ["1"] = ManifestConflictResolution.Skip,
            ["2"] = ManifestConflictResolution.Ignore,
            ["3"] = ManifestConflictResolution.IncludeUser,
            ["4"] = ManifestConflictResolution.UseOptions,
            ["s"] = ManifestConflictResolution.Skip,
            ["i"] = ManifestConflictResolution.Ignore,
            ["u"] = ManifestConflictResolution.IncludeUser,
            ["o"] = ManifestConflictResolution.UseOptions,
        },
        defaultResolution: ManifestConflictResolution.Skip);
}

static ManifestConflictResolution ResolveStartupConflictInteractively(
    string displayName,
    string startupFolder,
    OwnedOutputManifest manifest,
    StartupMergePlan plan)
{
    Helpers.WriteLine("");
    Helpers.WriteLine("ProgramStarter conflict:");
    Helpers.WriteLine($"- Item: {displayName}");
    Helpers.WriteLine($"- File: {plan.Fingerprint?.FilePath ?? "[not present]"}");
    Helpers.WriteLine($"- Manifest: {plan.Fingerprint?.ManifestPath ?? "[not present]"}");
    Helpers.WriteLine($"- Options: {plan.Fingerprint?.OptionsPath ?? Path.GetFileName(plan.TargetPath)}");
    Helpers.WriteLine($"- Reason: {plan.Message}");
    Helpers.WriteLine("");
    Helpers.WriteLine("[1] skip        Leave unresolved and report again later.");
    Helpers.WriteLine("[2] ignore      Suppress this exact conflict until it changes.");
    Helpers.WriteLine("[3] accept-file Make the current file the manifest baseline.");
    Helpers.WriteLine("[4] use-options Replace conflicting files with configured output.");
    Helpers.WriteLine("[5] forget-owned Remove manifest ownership and leave files alone.");
    return AskResolution(
        new Dictionary<string, ManifestConflictResolution>
        {
            ["1"] = ManifestConflictResolution.Skip,
            ["2"] = ManifestConflictResolution.Ignore,
            ["3"] = ManifestConflictResolution.AcceptFile,
            ["4"] = ManifestConflictResolution.UseOptions,
            ["5"] = ManifestConflictResolution.ForgetOwned,
            ["s"] = ManifestConflictResolution.Skip,
            ["i"] = ManifestConflictResolution.Ignore,
            ["a"] = ManifestConflictResolution.AcceptFile,
            ["u"] = ManifestConflictResolution.UseOptions,
            ["f"] = ManifestConflictResolution.ForgetOwned,
        },
        defaultResolution: ManifestConflictResolution.Skip);
}

static ManifestConflictResolution AskResolution(
    Dictionary<string, ManifestConflictResolution> choices,
    ManifestConflictResolution defaultResolution)
{
    while (true)
    {
        Console.Write("Resolution [skip] ");
        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(answer))
            return defaultResolution;
        if (choices.TryGetValue(answer, out var resolution))
            return resolution;
        Console.WriteLine("Please choose one of the listed numbers or shortcut letters.");
    }
}

static void AcceptStartupFile(string startupFolder, OwnedOutputManifest manifest, StartupMergePlan plan, string window)
{
    var filePath = plan.Fingerprint?.FilePath == null ? null : Path.Combine(startupFolder, plan.Fingerprint.FilePath);
    if (filePath == null || !File.Exists(filePath))
        return;

    manifest.RemoveByIdentity(plan.Identity);
    var entry = manifest.AdoptExisting(filePath, identity: plan.Identity);
    entry.Delay = plan.Fingerprint?.FileDelay;
    entry.Window = window;
}

static bool DeleteStartupConflictFiles(string startupFolder, StartupMergePlan plan)
{
    foreach (var relativePath in new[] { plan.Fingerprint?.FilePath, plan.Fingerprint?.ManifestPath }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase))
    {
        var path = Path.Combine(startupFolder, relativePath!);
        if (path.Equals(plan.TargetPath, StringComparison.OrdinalIgnoreCase))
            continue;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (UnauthorizedAccessException ex)
        {
            Helpers.WriteLine($"Could not resolve startup conflict: access denied deleting {relativePath} ({ex.Message})");
            return false;
        }
        catch (IOException ex)
        {
            Helpers.WriteLine($"Could not resolve startup conflict: delete failed for {relativePath} ({ex.Message})");
            return false;
        }
    }

    return true;
}

static string NormalizeStartupDelay(string? delay)
{
    if (string.IsNullOrWhiteSpace(delay))
        return "00m00s";

    var match = Regex.Match(delay.Trim(), @"^(?:(\d+)m)?(?:(\d+)s)?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50));
    if (!match.Success || (!match.Groups[1].Success && !match.Groups[2].Success))
        throw new ArgumentException($"Invalid startup delay '{delay}'. Use formats like 45s or 01m30s.");

    var minutes = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
    var seconds = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
    if (seconds >= 60)
        throw new ArgumentException($"Invalid startup delay '{delay}'. Seconds must be less than 60.");

    return minutes == 0
        ? $"{seconds:00}s"
        : $"{minutes:00}m{seconds:00}s";
}

static string NormalizeStartupWindow(string? window)
{
    if (string.IsNullOrWhiteSpace(window))
        return "preserve";

    var normalized = window.Trim().ToLowerInvariant();
    if (normalized is "preserve" or "normal" or "minimized" or "maximized")
        return normalized;

    throw new ArgumentException($"Invalid startup window '{window}'. Use preserve, normal, minimized, or maximized.");
}

static bool IsStartupCommand(string[] args)
    => args.Length >= 2
       && args[0].Equals("startup", StringComparison.OrdinalIgnoreCase);

static bool IsManifestCommand(string[] args)
    => args.Length >= 2
       && args[0].Equals("manifest", StringComparison.OrdinalIgnoreCase);

static void RunManifestCommand(string[] args, Settings options, OwnedOutputManifest rootManifest)
{
    var command = args.Length >= 2 ? args[1] : "";
    var isMergeIncludeUser = IsManifestMergeAutoResolve(args, "include-user");
    if (!isMergeIncludeUser)
    {
        Console.WriteLine($"Unknown manifest command '{command}'. Use merge --auto-resolve include-user, merge --auto-resolve ignore, or merge --interactive.");
        return;
    }

    var assumeYes = args.Any(arg => arg.Equals("--yes", StringComparison.OrdinalIgnoreCase) || arg.Equals("-y", StringComparison.OrdinalIgnoreCase));
    var candidates = new List<(OwnedOutputManifest Manifest, string Path, string Display, string? Identity)>();
    var startupFolder = Path.Combine(options.shortcuts, options.programStarter.folderName);
    var startupManifest = Directory.Exists(startupFolder)
        ? OwnedOutputManifest.Load(startupFolder, "program-starter.json", "MoveShortcuts ProgramStarter")
        : null;

    foreach (var path in Directory.EnumerateFiles(options.shortcuts, "*", SearchOption.AllDirectories))
    {
        if (IsManifestBookkeepingFile(path))
            continue;
        if (Helpers.IsPathInside(startupFolder, path))
            continue;
        if (!rootManifest.IsOwned(path))
            candidates.Add((rootManifest, path, Path.GetRelativePath(options.shortcuts, path), null));
    }

    if (startupManifest != null)
    {
        foreach (var path in Directory.EnumerateFiles(startupFolder))
        {
            if (IsManifestBookkeepingFile(path))
                continue;
            if (startupManifest.IsOwned(path))
                continue;

            var identity = TryGetStartupLogicalIdentity(path, out var startupIdentity)
                ? startupIdentity
                : null;
            candidates.Add((startupManifest, path, Path.Combine(options.programStarter.folderName, Path.GetFileName(path)), identity));
        }
    }

    if (candidates.Count == 0)
    {
        Console.WriteLine("No unowned files were found in managed folders.");
        return;
    }

    Console.WriteLine("The following user-owned files will be included in MoveShortcuts manifests:");
    foreach (var candidate in candidates.OrderBy(c => c.Display, StringComparer.OrdinalIgnoreCase))
        Console.WriteLine("  " + candidate.Display);

    if (!assumeYes && !AskYesNo("Include these files in the ownership manifests?", defaultValue: false))
    {
        Console.WriteLine("Manifest inclusion cancelled. No ownership changed.");
        return;
    }

    foreach (var candidate in candidates)
        candidate.Manifest.AdoptExisting(candidate.Path, identity: candidate.Identity);

    rootManifest.Save();
    startupManifest?.Save();
    Console.WriteLine($"Included {candidates.Count} file(s) in ownership manifests.");
}

static bool IsManifestMergeAutoResolve(string[] args, string resolution)
    => args.Length >= 2
       && args[0].Equals("manifest", StringComparison.OrdinalIgnoreCase)
       && args[1].Equals("merge", StringComparison.OrdinalIgnoreCase)
       && HasOptionValue(args, "--auto-resolve", resolution);

static bool IsManifestMergeInteractive(string[] args)
    => args.Length >= 2
       && args[0].Equals("manifest", StringComparison.OrdinalIgnoreCase)
       && args[1].Equals("merge", StringComparison.OrdinalIgnoreCase)
       && args.Any(arg => arg.Equals("--interactive", StringComparison.OrdinalIgnoreCase));

static bool HasOptionValue(string[] args, string optionName, string expectedValue)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)
            && i + 1 < args.Length
            && args[i + 1].Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            return true;

        if (args[i].StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase)
            && args[i][(optionName.Length + 1)..].Equals(expectedValue, StringComparison.OrdinalIgnoreCase))
            return true;
    }

    return false;
}

static bool IsManifestBookkeepingFile(string path)
{
    var name = Path.GetFileName(path);
    return name.Equals("move-shortcuts-manifest.json", StringComparison.OrdinalIgnoreCase)
           || name.Equals("program-starter.json", StringComparison.OrdinalIgnoreCase)
           || name.Equals("program-starter.log", StringComparison.OrdinalIgnoreCase)
           || name.Equals("move-shortcuts.log", StringComparison.OrdinalIgnoreCase);
}

static void RunStartupCommand(string[] args, Settings options)
{
    var command = args.Length >= 2 ? args[1] : "status";
    if (command.Equals("status", StringComparison.OrdinalIgnoreCase))
    {
        PrintProgramStarterStatus(options);
        return;
    }

    if (command.Equals("install", StringComparison.OrdinalIgnoreCase))
    {
        EnsureProgramStarterInstalled(options, askIfMissing: false);
        return;
    }

    if (command.Equals("uninstall", StringComparison.OrdinalIgnoreCase))
    {
        UninstallProgramStarter();
        return;
    }

    if (command.Equals("run", StringComparison.OrdinalIgnoreCase))
    {
        RunProgramStarterNow(options, visible: true);
        return;
    }

    Console.WriteLine($"Unknown startup command '{command}'. Use status, install, uninstall, or run.");
}

static void PrintProgramStarterStatus(Settings options)
{
    Console.WriteLine("MoveShortcuts ProgramStarter");
    Console.WriteLine($"  Enabled in config: {options.programStarter.enabled}");
    Console.WriteLine($"  Folder: {Path.Combine(options.shortcuts, options.programStarter.folderName)}");
    Console.WriteLine($"  Startup item: {GetProgramStarterStartupShortcutPath()}");
    Console.WriteLine($"  Startup item exists: {File.Exists(GetProgramStarterStartupShortcutPath())}");
    Console.WriteLine($"  Runner: {GetProgramStarterExecutablePath()}");
}

static void EnsureProgramStarterInstalled(Settings options, bool askIfMissing = false)
{
    var executablePath = GetProgramStarterExecutablePath();
    if (!File.Exists(executablePath))
    {
        Helpers.WriteLine($"ProgramStarter was not found next to MoveShortcuts: {executablePath}");
        return;
    }

    var startupShortcut = GetProgramStarterStartupShortcutPath();
    if (askIfMissing && !File.Exists(startupShortcut))
    {
        if (!AskYesNo("ProgramStarter is enabled, but its Windows Startup item is missing. Reinstall it?", defaultValue: true))
        {
            if (AskYesNo("Turn off ProgramStarter in the options file now?", defaultValue: false))
                SetProgramStarterEnabled(false);
            return;
        }
    }

    Directory.CreateDirectory(Path.GetDirectoryName(startupShortcut)!);
    var hidden = options.programStarter.runnerWindow.Equals("hidden", StringComparison.OrdinalIgnoreCase);
    var startupFolder = Path.Combine(options.shortcuts, options.programStarter.folderName);
    var target = hidden ? "powershell.exe" : executablePath;
    var arguments = hidden
        ? $"-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -Command \"& '{executablePath.Replace("'", "''")}' --start-now --folder '{startupFolder.Replace("'", "''")}'\""
        : $"--start-now --folder \"{startupFolder}\" --visible";

    if (Helpers.CreateShortcut(startupShortcut, target, arguments: arguments))
        Helpers.WriteLine($"ProgramStarter startup item is installed: {startupShortcut}");
    else
        Helpers.WriteLine($"Could not install ProgramStarter startup item: {startupShortcut}");
}

static void SetProgramStarterEnabled(bool enabled)
{
    const string optsFileName = "move-shortcuts-options.json";
    if (!File.Exists(optsFileName))
        return;

    var settings = JsonConvert.DeserializeObject<Settings>(File.ReadAllText(optsFileName)) ?? new Settings();
    InitOptionsBuilder.NormalizeSettings(settings);
    settings.programStarter.enabled = enabled;
    File.WriteAllText(optsFileName, JsonConvert.SerializeObject(settings, Formatting.Indented));
    Console.WriteLine($"Updated {optsFileName}: programStarter.enabled = {enabled.ToString().ToLowerInvariant()}");
}

static void UninstallProgramStarter()
{
    var startupShortcut = GetProgramStarterStartupShortcutPath();
    if (!File.Exists(startupShortcut))
    {
        Console.WriteLine("ProgramStarter startup item is not installed.");
        return;
    }

    File.Delete(startupShortcut);
    Console.WriteLine("ProgramStarter startup item removed.");
}

static void RunProgramStarterNow(Settings options, bool visible)
{
    var executablePath = GetProgramStarterExecutablePath();
    if (!File.Exists(executablePath))
    {
        Console.WriteLine($"ProgramStarter was not found next to MoveShortcuts: {executablePath}");
        return;
    }

    var startupFolder = Path.Combine(options.shortcuts, options.programStarter.folderName);
    var arguments = visible
        ? $"--start-now --folder \"{startupFolder}\" --visible"
        : $"--start-now --folder \"{startupFolder}\"";
    using var _ = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
    {
        FileName = executablePath,
        Arguments = arguments,
        UseShellExecute = true,
    });
}

static string GetProgramStarterExecutablePath()
    => Path.Combine(AppContext.BaseDirectory, "ProgramStarter.exe");

static string GetProgramStarterStartupShortcutPath()
    => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "MoveShortcuts ProgramStarter.lnk");

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

static bool IsEditRequested(string[] args)
{
    return args.Any(arg => arg.Equals("edit", StringComparison.OrdinalIgnoreCase));
}

static bool IsRefreshUwpCacheRequested(string[] args)
{
    return args.Any(arg => arg.Equals("--refresh-uwp-cache", StringComparison.OrdinalIgnoreCase));
}

static bool IsPathOnlyRequest(string[] args, PathPlacement? userPathRequest, PathPlacement? machinePathRequest)
{
    if (!userPathRequest.HasValue && !machinePathRequest.HasValue)
        return false;

    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.StartsWith("--add-user-path=", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--add-machine-path=", StringComparison.OrdinalIgnoreCase))
            continue;

        if (arg.Equals("--add-user-path", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--add-machine-path", StringComparison.OrdinalIgnoreCase))
        {
            if (i + 1 < args.Length && IsPathPlacementValue(args[i + 1]))
                i++;
            continue;
        }

        if (arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--log", StringComparison.OrdinalIgnoreCase) ||
            arg.Equals("--cli", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith("--progress=", StringComparison.OrdinalIgnoreCase))
            continue;

        if (arg.Equals("--progress", StringComparison.OrdinalIgnoreCase))
        {
            i++;
            continue;
        }

        return false;
    }

    return true;
}

static PathPlacement? GetPathPlacementRequest(string[] args, string optionName)
{
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
            return ParsePathPlacement(arg[(optionName.Length + 1)..]);

        if (!arg.Equals(optionName, StringComparison.OrdinalIgnoreCase))
            continue;

        if (i + 1 < args.Length && TryParsePathPlacement(args[i + 1], out var placement))
            return placement;

        return PathPlacement.AppendIfMissing;
    }

    return null;
}

static bool IsPathPlacementValue(string arg)
    => arg.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
       arg.Equals("first", StringComparison.OrdinalIgnoreCase) ||
       arg.Equals("last", StringComparison.OrdinalIgnoreCase);

static void RunEdit(string optsFileName)
{
    var optionsPath = Path.GetFullPath(optsFileName);
    if (!File.Exists(optionsPath))
    {
        Console.WriteLine("No data file found: " + optsFileName);
        Console.WriteLine("Run MoveShortcuts init to create one.");
        return;
    }

    try
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = optionsPath,
            UseShellExecute = true,
        });
    }
    catch
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "notepad.exe",
            Arguments = "\"" + optionsPath + "\"",
            UseShellExecute = true,
        });
    }
}

static void ApplyPathSettings(Settings options, string shortcuts, PathPlacement? userPathRequest, PathPlacement? machinePathRequest)
{
    var addToUserPath = options.path.addToUserPath || userPathRequest.HasValue;
    var addToMachinePath = options.path.addToMachinePath || machinePathRequest.HasValue;
    var userPlacement = userPathRequest ?? ParsePathPlacement(options.path.userPathPlacement);
    var machinePlacement = machinePathRequest ?? ParsePathPlacement(options.path.machinePathPlacement);

    if (addToUserPath)
    {
        WritePathUpdateResult(
            PathEnvironmentManager.AddToUserPath(shortcuts, userPlacement),
            shortcuts,
            "current user PATH");
    }

    if (addToMachinePath)
    {
        WritePathUpdateResult(
            PathEnvironmentManager.AddToMachinePathElevated(shortcuts, machinePlacement),
            shortcuts,
            "machine PATH");
    }
}

static void WritePathUpdateResult(PathUpdateStatus status, string shortcuts, string targetName)
{
    switch (status)
    {
        case PathUpdateStatus.AlreadyPresent:
            Helpers.WriteLine($"{shortcuts} is already in the {targetName}.");
            break;
        case PathUpdateStatus.Updated:
            Helpers.WriteLine($"Added {shortcuts} to the {targetName}.");
            break;
        case PathUpdateStatus.CancelledOrFailed:
            Helpers.WriteLine($"Could not add {shortcuts} to the {targetName}; the elevated update may have been cancelled.");
            break;
    }
}

static PathPlacement ParsePathPlacement(string? value)
{
    if (TryParsePathPlacement(value, out var placement))
        return placement;

    throw new ArgumentException($"Unknown PATH placement '{value}'. Use auto, first, or last.");
}

static bool TryParsePathPlacement(string? value, out PathPlacement placement)
{
    placement = PathPlacement.AppendIfMissing;
    if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        return true;

    if (value.Equals("first", StringComparison.OrdinalIgnoreCase))
    {
        placement = PathPlacement.PrependOrMove;
        return true;
    }

    if (value.Equals("last", StringComparison.OrdinalIgnoreCase))
    {
        placement = PathPlacement.AppendOrMove;
        return true;
    }

    return false;
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
        path = new PathSettings(),
        programStarter = new ProgramStarterSettings(),
        fileOptions = new Dictionary<string, MyFileOptions>(),
    };

    if (!PathEnvironmentManager.IsDirectoryInTargetPath(options.shortcuts, EnvironmentVariableTarget.User))
    {
        options.path.addToUserPath = AskYesNo(
            "Add the Shortcuts folder to your current user PATH?",
            defaultValue: true);
    }

    if (!PathEnvironmentManager.IsDirectoryInTargetPath(options.shortcuts, EnvironmentVariableTarget.Machine))
    {
        options.path.addToMachinePath = AskYesNo(
            "Also add it to the machine PATH for elevated/admin terminals? This will request UAC when the tool runs.",
            defaultValue: false);
    }

    options.cleanup.deleteDesktopShortcuts = options.sources.desktop
        && AskYesNo(
            "MoveShortcuts can unclutter your desktop from installer icons. Remove matching desktop shortcuts after managed shortcuts are created?",
            defaultValue: false);

    options.aliases.generateInitials = AskYesNo(
        "Generate short aliases from words and capital letters? Examples: Visual Studio Code -> vsc, FireFox -> ff.",
        defaultValue: true);

    if (options.aliases.generateInitials)
        options.aliases.minimumLength = AskInt("Minimum generated alias length", defaultValue: 2, minimum: 1);

    options.programStarter.enabled = AskYesNo(
        "Use ProgramStarter for delayed startup shortcuts? It creates one Windows Startup item and launches configured shortcuts from a timed folder.",
        defaultValue: false);

    var addSelfReference = AskYesNo(
        "Add MoveShortcuts self commands to the options file? Suggested command: mvshct.",
        defaultValue: true);
    var selfCommandName = SelfReferenceBuilder.DefaultCommandName;
    if (addSelfReference)
        selfCommandName = AskString("MoveShortcuts command name", SelfReferenceBuilder.DefaultCommandName);

    Helpers.WriteLine("");
    Helpers.WriteLine("Scanning selected sources...");
    var initScan = ScanForInit(options);
    options.fileOptions = initScan.FileOptions;
    if (addSelfReference)
    {
        var selfOptions = SelfReferenceBuilder.CreateSelfReferenceOptions(
            selfCommandName,
            selfCommandName + "-edit",
            System.Reflection.Assembly.GetExecutingAssembly().Location,
            Environment.ProcessPath);
        foreach (var kv in selfOptions)
            options.fileOptions[kv.Key] = kv.Value;
    }

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

    if (options.path.addToUserPath)
        Helpers.WriteLine($"- {options.shortcuts} will be added to the current user PATH when MoveShortcuts runs");
    if (options.path.addToMachinePath)
        Helpers.WriteLine($"- {options.shortcuts} will be added to the machine PATH using a one-time elevated helper when MoveShortcuts runs");
    if (addSelfReference)
        Helpers.WriteLine($"- self commands will be added: {selfCommandName}.cmd and {selfCommandName}-edit.cmd");
    if (options.programStarter.enabled)
        Helpers.WriteLine($"- delayed startup shortcuts can be generated in {Path.Combine(options.shortcuts, options.programStarter.folderName)}");

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
          MoveShortcuts startup <status|install|uninstall|run>
          MoveShortcuts manifest merge --auto-resolve include-user
          MoveShortcuts manifest merge --auto-resolve ignore
          MoveShortcuts manifest merge --interactive

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

          --add-user-path
              Add the configured Shortcuts folder to the current user PATH.
              Optional placement: auto, first, last.

          --add-machine-path
              Add the configured Shortcuts folder to the machine PATH using a one-time UAC prompt.
              Optional placement: auto, first, last.

        Caches:
          move-shortcuts-uwp-cache.json
              Stores AppsFolder enumeration while the Windows package signature is unchanged.

          move-shortcuts-target-cache.json
              Stores resolved external targets such as "es tool.exe" while the target still exists.

        Configuration:
          Reads move-shortcuts-options.json from the current working directory.
          The config file can also set "progress": "auto", "quiet", "log", or "cli".

        Commands:
          init
              Ask a few questions, scan selected sources, estimate the effect,
              and create move-shortcuts-options.json without creating shortcuts.

          edit
              Open move-shortcuts-options.json with the default editor, falling back to Notepad.

          startup status
              Show ProgramStarter configuration, folder, runner, and Startup item status.

          startup install
              Install the MoveShortcuts ProgramStarter item in the current user's Windows Startup folder.

          startup uninstall
              Remove the MoveShortcuts ProgramStarter item from the current user's Windows Startup folder.

          startup run
              Ask ProgramStarter to run the delayed startup queue now.

          manifest merge --auto-resolve include-user
              List unowned files in managed folders and ask for clearance to
              include them in ownership manifests. Use --yes to skip the prompt.

          manifest merge --auto-resolve ignore
              Suppress exact current conflicts without changing ownership or
              generated files. The ignore is cleared when that conflict changes.

          manifest merge --interactive
              Prompt for each manifest conflict and choose skip, ignore,
              include/accept file, use options, or forget ownership.

        ProgramStarter:
          Add "programStarter": { "enabled": true } and per-entry Startup
          settings to generate delayed startup shortcuts. MoveShortcuts writes
          them into the ProgramStarter folder using names such as 01m30s_App.lnk.

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
