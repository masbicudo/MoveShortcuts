namespace MoveShortcuts
{
    [Flags]
    public enum FileAction
    {
        None = 0,

        /// <summary>
        /// Copy shortcut if already a link (.LNK .URL),
        /// or creates a new link at shortcuts folder
        /// </summary>
        MakeShortcut = 1,

        /// <summary>
        /// Delete source if it is a link (.LNK .URL) and source path is at desktop
        /// </summary>
        DeleteDesktopLink = 2,

        /// <summary>
        /// Creates a folder link if it's a folder, using multiple formats
        /// for PowerShell, Cmd, Bash and others that are available
        /// </summary>
        FolderLink = 4,
    }
}