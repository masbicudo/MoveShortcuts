// See https://aka.ms/new-console-template for more information
using MoveShortcuts;
using System.Text.RegularExpressions;

var fileOptions = _Settings.fileOptions;
var shortcuts = _Settings.shortcuts;

//foreach (var key in fileOptions.Keys.ToArray())
//    if (!key.Contains("Visual Studio Code"))
//        fileOptions.Remove(key);

// ensuring shortcuts directory is there
Directory.CreateDirectory(shortcuts);

//
// Collecting files to process
//
var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
var desktopPath2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
var startMenuPath2 = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

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
}

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
        var keyMatch = fileOptions.Keys.FirstOrDefault(
            k => Regex.IsMatch(fileName, "^" + k + "$", RegexOptions.IgnoreCase));
        if (keyMatch != null)
            opts = fileOptions[keyMatch];
    }
    if (opts != null)
    {
        actionsList.Add(new(file, fileName, opts));
    }
}
actionsList.Sort(
        (a1, a2) => FileNameComparer.Default.Compare(
            a1.FileName,
            a2.FileName)
    );

Console.WriteLine("Processing files:");
foreach (var action in Helpers.LogProgress(actionsList, a => a.FileName))
{
    var currentPath = action.FullPath;
    if ((action.Options.Action & FileAction.MakeShortcut) != 0)
    {
        if (Helpers.HasExt(currentPath, ".lnk", ".url"))
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
            if (File.Exists(pathAtDesktop))
                File.Delete(pathAtDesktop);
        }
        catch (FileNotFoundException)
        {
        }

        var fileNameExt2 = Path.GetFileName(action.FullPath);
        var pathAtDesktop2 = Path.Combine(desktopPath2, fileNameExt);
        try
        {
            if (File.Exists(pathAtDesktop2))
                File.Delete(pathAtDesktop2);
        }
        catch (FileNotFoundException)
        {
        }
    }
}