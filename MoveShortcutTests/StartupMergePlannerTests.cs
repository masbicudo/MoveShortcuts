using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class StartupMergePlannerTests
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
        public void Plan_BlocksGeneratedEntry_WhenUserOwnedSameNameHasDifferentDelay()
        {
            File.WriteAllText(Path.Combine(_tempRoot, "03m20s_XPTO.cmd"), "manual");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "program-starter.json", "test");

            var plan = StartupMergePlanner.Plan(_tempRoot, manifest, "XPTO", ".cmd", "02m50s");

            Assert.AreEqual(StartupMergeStatus.Conflict, plan.Status);
            StringAssert.Contains(plan.Message, "user-owned startup file");
        }

        [TestMethod]
        public void Plan_RenamesOwnedFile_WhenBaseFileUnchangedAndOptionsChanged()
        {
            var oldPath = Path.Combine(_tempRoot, "03m30s_XPTO.cmd");
            File.WriteAllText(oldPath, "owned");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "program-starter.json", "test");
            var entry = manifest.Touch(oldPath, identity: "startup:XPTO.cmd");
            entry.Delay = "03m30s";

            var plan = StartupMergePlanner.Plan(_tempRoot, manifest, "XPTO", ".cmd", "04m30s");

            Assert.AreEqual(StartupMergeStatus.Write, plan.Status);
            Assert.AreEqual(oldPath, plan.OldOwnedPath);
            Assert.AreEqual(Path.Combine(_tempRoot, "04m30s_XPTO.cmd"), plan.TargetPath);
        }

        [TestMethod]
        public void Plan_Conflicts_WhenUserRenamedOwnedFileAndOptionsChanged()
        {
            var oldPath = Path.Combine(_tempRoot, "03m10s_XPTO.cmd");
            var userRenamedPath = Path.Combine(_tempRoot, "02m40s_XPTO.cmd");
            File.WriteAllText(userRenamedPath, "manual rename");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "program-starter.json", "test");
            var entry = manifest.Touch(oldPath, identity: "startup:XPTO.cmd");
            entry.Delay = "03m10s";

            var plan = StartupMergePlanner.Plan(_tempRoot, manifest, "XPTO", ".cmd", "04m30s");

            Assert.AreEqual(StartupMergeStatus.Conflict, plan.Status);
            StringAssert.Contains(plan.Message, "options also changed");
            Assert.AreEqual("03m10s_XPTO.cmd", plan.Fingerprint?.ManifestPath);
            Assert.AreEqual("02m40s_XPTO.cmd", plan.Fingerprint?.FilePath);
            Assert.AreEqual("04m30s_XPTO.cmd", plan.Fingerprint?.OptionsPath);
        }

        [TestMethod]
        public void Plan_Conflicts_WhenOwnedFileContentPathNoLongerMatchesManifestDelay()
        {
            var path = Path.Combine(_tempRoot, "02m40s_XPTO.cmd");
            File.WriteAllText(path, "changed");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "program-starter.json", "test");
            var entry = manifest.Touch(path, identity: "startup:XPTO.cmd");
            entry.Delay = "03m10s";

            var plan = StartupMergePlanner.Plan(_tempRoot, manifest, "XPTO", ".cmd", "04m30s");

            Assert.AreEqual(StartupMergeStatus.Conflict, plan.Status);
            StringAssert.Contains(plan.Message, "changed outside MoveShortcuts");
        }
    }
}
