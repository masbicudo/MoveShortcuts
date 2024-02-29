namespace MoveShortcuts
{
    public class MyFileOptions
    {
        public FileAction Action { get; set; }
            = FileAction.MakeShortcut
            | FileAction.DeleteDesktopLink
            | FileAction.FolderLink
            | FileAction.InternetLink;

        /// <summary>
        /// Instructs the creation of alternative names for a given file or folder
        /// </summary>
        public List<string> AltNames { get; } = new List<string>();
        public List<string> ElevNames { get; } = new List<string>();
        public string Target { get; set; } = null;
    }
}