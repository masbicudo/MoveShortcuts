using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class ProxyScriptTests
    {
        private string _tempRoot = "";

        [TestInitialize]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MoveShortcutsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [TestMethod]
        public void CreateCommandPromptProxy_UsesPushdAndPreservesExitCode_WhenWorkdirIsSet()
        {
            var proxy = Path.Combine(_tempRoot, "tool.cmd");

            Helpers.CreateCommandPromptProxy(proxy, @"C:\Tools\tool.exe", elevated: false, workdir: @"D:\Config", arguments: "--config");

            var script = File.ReadAllText(proxy);
            StringAssert.Contains(script, @"pushd ""D:\Config""");
            StringAssert.Contains(script, @"""C:\Tools\tool.exe"" --config %*");
            StringAssert.Contains(script, @"set ""exitCode=%ERRORLEVEL%""");
            StringAssert.Contains(script, "popd");
            StringAssert.Contains(script, "exit /b %exitCode%");
        }
    }
}
