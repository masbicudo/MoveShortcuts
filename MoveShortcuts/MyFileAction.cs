namespace MoveShortcuts
{
    public class MyFileAction
    {
        public MyFileAction(string fullPath, string fileName, MyFileOptions options, bool isNotReady)
        {
            FullPath = fullPath;
            FileName = fileName;
            Options = options;
            IsNotReady = isNotReady;
        }

        public string FullPath { get; set; }
        public string FileName { get; set; }
        public MyFileOptions Options { get; set; }
        public bool IsNotReady { get; set; }

        public override string ToString()
        {
            return FileName;
        }
    }
}