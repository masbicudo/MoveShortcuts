using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class ShortcutShadowingTests
    {
        private string _tempRoot = "";
        private string _shortcuts = "";
        private string _externalBin = "";

        [TestInitialize]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MoveShortcutsTests", Guid.NewGuid().ToString("N"));
            _shortcuts = Path.Combine(_tempRoot, "Shortcuts");
            _externalBin = Path.Combine(_tempRoot, "ExternalBin");
            Directory.CreateDirectory(_shortcuts);
            Directory.CreateDirectory(_externalBin);
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [TestMethod]
        public void WouldShadowExternalCommand_ReturnsTrue_WhenCommandExistsOutsideShortcuts()
        {
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");

            var shadows = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                outputPath,
                _externalBin,
                ".EXE;.LNK");

            Assert.IsTrue(shadows);
        }

        [TestMethod]
        public void GetExternalCommandConflict_ReturnsFirstExternalCommandPath()
        {
            var externalCommand = Path.Combine(_externalBin, "ollama.exe");
            File.WriteAllText(externalCommand, "");
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");

            var conflict = Helpers.GetExternalCommandConflict(
                _shortcuts,
                outputPath,
                _externalBin,
                ".EXE;.LNK");

            Assert.AreEqual(Path.GetFullPath(externalCommand), conflict, ignoreCase: true);
        }

        [TestMethod]
        public void WouldShadowExternalCommand_ReturnsFalse_WhenCommandOnlyExistsInsideShortcuts()
        {
            File.WriteAllText(Path.Combine(_shortcuts, "ollama.lnk"), "");
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");

            var shadows = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                outputPath,
                _shortcuts,
                ".EXE;.LNK");

            Assert.IsFalse(shadows);
        }

        [TestMethod]
        public void WouldShadowExternalCommand_ReturnsFalse_WhenShortcutResolvesBeforeExternalCommand()
        {
            File.WriteAllText(Path.Combine(_shortcuts, "ollama.lnk"), "");
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");
            var pathValue = string.Join(Path.PathSeparator, _shortcuts, _externalBin);

            var shadows = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                outputPath,
                pathValue,
                ".EXE;.LNK");

            Assert.IsFalse(shadows);
        }

        [TestMethod]
        public void WouldShadowExternalCommand_SeesShortcutCreatedAfterPathIndexWasCached()
        {
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var pathValue = string.Join(Path.PathSeparator, _shortcuts, _externalBin);
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");

            var shadowsBeforeShortcutExists = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                outputPath,
                pathValue,
                ".EXE;.LNK");
            File.WriteAllText(outputPath, "");
            var shadowsAfterShortcutExists = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                Path.Combine(_shortcuts, "ollama.ps1"),
                pathValue,
                ".EXE;.LNK");

            Assert.IsTrue(shadowsBeforeShortcutExists);
            Assert.IsFalse(shadowsAfterShortcutExists);
        }

        [TestMethod]
        public void WouldShadowExternalCommand_ReturnsFalse_ForNonConflictingAlias()
        {
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var outputPath = Path.Combine(_shortcuts, "ollama-app.lnk");

            var shadows = Helpers.WouldShadowExternalCommand(
                _shortcuts,
                outputPath,
                _externalBin,
                ".EXE;.LNK");

            Assert.IsFalse(shadows);
        }

        [TestMethod]
        public void CopyShortcutOutput_SkipsRootShortcut_WhenItWouldShadowExternalCommand()
        {
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var source = Path.Combine(_tempRoot, "VendorOllama.lnk");
            File.WriteAllText(source, "vendor shortcut");
            var outputPath = Path.Combine(_shortcuts, "ollama.lnk");
            var originalPath = Environment.GetEnvironmentVariable("PATH");
            var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

            try
            {
                Environment.SetEnvironmentVariable("PATH", _externalBin);
                Environment.SetEnvironmentVariable("PATHEXT", ".EXE;.LNK");

                var copied = Helpers.CopyShortcutOutput(_shortcuts, source, outputPath);

                Assert.IsFalse(copied);
                Assert.IsFalse(File.Exists(outputPath));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
                Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
            }
        }

        [TestMethod]
        public void PlainCopy_AllowsGroupCopiesEvenWhenRootNameWouldShadowExternalCommand()
        {
            File.WriteAllText(Path.Combine(_externalBin, "ollama.exe"), "");
            var source = Path.Combine(_tempRoot, "VendorOllama.lnk");
            File.WriteAllText(source, "vendor shortcut");
            var groupPath = Path.Combine(_shortcuts, "IA", "ollama.lnk");
            Directory.CreateDirectory(Path.GetDirectoryName(groupPath)!);

            Helpers.Copy(source, groupPath);

            Assert.IsTrue(File.Exists(groupPath));
        }
    }
}
