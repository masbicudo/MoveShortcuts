// See https://aka.ms/new-console-template for more information

// Adding actions for UWP apps that are installed
// GUID taken from https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid
using Microsoft.WindowsAPICodePack.Shell;

var FODLERID_AppsFolder = new Guid("{1e87508d-89c2-42f0-8a7e-645a0f50ca58}");
ShellObject appsFolder = (ShellObject)KnownFolderHelper.FromKnownFolderId(FODLERID_AppsFolder);
Dictionary<string, string> appNames = new();
foreach (var app in (IKnownFolder)appsFolder)
{
    string name = app.Name;
    string appUserModelID = app.ParsingName;
    Console.WriteLine(name);
    Console.WriteLine(appUserModelID);
}
