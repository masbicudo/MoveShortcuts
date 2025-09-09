namespace MoveShortcuts
{
    public class MyFileOptions
    {
        public FileAction Action { get; set; }
            = FileAction.MakeShortcut
            | FileAction.DeleteDesktopLink
            | FileAction.FolderLink
            | FileAction.InternetLink
            | FileAction.FileLink;

        /// <summary>
        /// Instructs the creation of alternative names for a given file or folder
        /// </summary>
        public List<string> AltNames { get; } = new List<string>();
        public List<string> ElevNames { get; } = new List<string>();
        public string Target { get; set; } = null;
        public List<string> Groups { get; } = new List<string>();
        public string WorkDir { get; set; } = null;
        /// <summary>
        /// List of link types to create.
        /// Defaults to ["lnk"].
        /// Supported values are "lnk", "ps1", "cmd", "sh".
        /// </summary>
        public List<string> LinkTypes { get; set; } = new List<string>();
    }
}