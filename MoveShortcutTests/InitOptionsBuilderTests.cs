using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class InitOptionsBuilderTests
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
        [DataRow("Visual Studio Code", "vsc")]
        [DataRow("FireFox", "ff")]
        [DataRow("Fire fox", "ff")]
        [DataRow("CrystalDiskInfo", "cdi")]
        [DataRow("GitHub Desktop", "ghd")]
        public void GenerateInitialsAlias_UsesWordsAndCapitalBoundaries(string name, string expected)
        {
            var alias = InitOptionsBuilder.GenerateInitialsAlias(name);

            Assert.AreEqual(expected, alias);
        }

        [TestMethod]
        public void CreateFileOptions_ExcludesDesktopDeletionByDefault()
        {
            var options = InitOptionsBuilder.CreateFileOptions(deleteDesktopShortcuts: false);

            Assert.IsFalse(options.Action.HasFlag(FileAction.DeleteDesktopLink));
            Assert.IsTrue(options.Action.HasFlag(FileAction.MakeShortcut));
        }

        [TestMethod]
        public void CreateFileOptions_IncludesDesktopDeletionWhenRequested()
        {
            var options = InitOptionsBuilder.CreateFileOptions(deleteDesktopShortcuts: true);

            Assert.IsTrue(options.Action.HasFlag(FileAction.DeleteDesktopLink));
        }

        [TestMethod]
        public void TryAddInitialsAlias_AddsNonConflictingAlias()
        {
            var option = InitOptionsBuilder.CreateFileOptions(deleteDesktopShortcuts: false);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Visual Studio Code" };

            var result = InitOptionsBuilder.TryAddInitialsAlias(
                option,
                "Visual Studio Code",
                reserved,
                _shortcuts,
                minimumLength: 2);

            Assert.AreEqual(InitAliasStatus.Added, result.Status);
            CollectionAssert.Contains(option.AltNames, "vsc");
        }

        [TestMethod]
        public void TryAddInitialsAlias_SkipsAliasBelowMinimumLength()
        {
            var option = InitOptionsBuilder.CreateFileOptions(deleteDesktopShortcuts: false);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Word" };

            var result = InitOptionsBuilder.TryAddInitialsAlias(
                option,
                "Word",
                reserved,
                _shortcuts,
                minimumLength: 2);

            Assert.AreEqual(InitAliasStatus.NotGenerated, result.Status);
            Assert.AreEqual(0, option.AltNames.Count);
        }

        [TestMethod]
        public void TryAddInitialsAlias_SkipsExternalCommandConflict()
        {
            File.WriteAllText(Path.Combine(_externalBin, "vsc.exe"), "");
            var option = InitOptionsBuilder.CreateFileOptions(deleteDesktopShortcuts: false);
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Visual Studio Code" };
            var originalPath = Environment.GetEnvironmentVariable("PATH");
            var originalPathExt = Environment.GetEnvironmentVariable("PATHEXT");

            try
            {
                Environment.SetEnvironmentVariable("PATH", _externalBin);
                Environment.SetEnvironmentVariable("PATHEXT", ".EXE;.LNK");

                var result = InitOptionsBuilder.TryAddInitialsAlias(
                    option,
                    "Visual Studio Code",
                    reserved,
                    _shortcuts,
                    minimumLength: 2);

                Assert.AreEqual(InitAliasStatus.Skipped, result.Status);
                Assert.AreEqual(0, option.AltNames.Count);
                Assert.IsFalse(reserved.Contains("vsc"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", originalPath);
                Environment.SetEnvironmentVariable("PATHEXT", originalPathExt);
            }
        }

        [TestMethod]
        public void ApplyGlobalCleanupSetting_RemovesDesktopDeletionWhenDisabled()
        {
            var settings = new Settings
            {
                cleanup = new CleanupSettings { deleteDesktopShortcuts = false },
                fileOptions = new Dictionary<string, MyFileOptions>
                {
                    ["App"] = new() { Action = FileAction.MakeShortcut | FileAction.DeleteDesktopLink }
                }
            };

            InitOptionsBuilder.ApplyGlobalCleanupSetting(settings);

            Assert.IsFalse(settings.fileOptions["App"].Action.HasFlag(FileAction.DeleteDesktopLink));
        }
    }
}
