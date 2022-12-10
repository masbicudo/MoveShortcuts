// See https://aka.ms/new-console-template for more information

namespace MoveShortcuts
{
    public class MyFileAction
    {
        public MyFileAction(string fullPath, string fileName, MyFileOptions options)
        {
            FullPath = fullPath;
            FileName = fileName;
            Options = options;
        }

        public string FullPath { get; set; }
        public string FileName { get; set; }
        public MyFileOptions Options { get; set; }

        public override string ToString()
        {
            return FileName;
        }
    }
}