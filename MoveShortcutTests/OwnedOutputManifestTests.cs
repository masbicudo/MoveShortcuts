using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class OwnedOutputManifestTests
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
        public void CanWrite_AllowsMissingFile()
        {
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");

            Assert.IsTrue(manifest.CanWrite(Path.Combine(_tempRoot, "new.lnk")));
        }

        [TestMethod]
        public void CanWrite_BlocksExistingUnownedFile()
        {
            var path = Path.Combine(_tempRoot, "manual.lnk");
            File.WriteAllText(path, "manual");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");

            Assert.IsFalse(manifest.CanWrite(path));
        }

        [TestMethod]
        public void CanWrite_AllowsExistingOwnedFileAfterReload()
        {
            var path = Path.Combine(_tempRoot, "owned.lnk");
            File.WriteAllText(path, "owned");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            manifest.Touch(path);
            manifest.Save();

            var reloaded = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");

            Assert.IsTrue(reloaded.CanWrite(path));
        }

        [TestMethod]
        public void RemoveStaleTouchedScope_RemovesOnlyOwnedUntouchedFiles()
        {
            var owned = Path.Combine(_tempRoot, "owned.lnk");
            var kept = Path.Combine(_tempRoot, "kept.lnk");
            var manual = Path.Combine(_tempRoot, "manual.lnk");
            File.WriteAllText(owned, "owned");
            File.WriteAllText(kept, "kept");
            File.WriteAllText(manual, "manual");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            manifest.Touch(owned);
            manifest.Touch(kept);
            manifest.Save();

            var reloaded = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            reloaded.Touch(kept);
            reloaded.RemoveStaleTouchedScope();

            Assert.IsFalse(File.Exists(owned));
            Assert.IsTrue(File.Exists(kept));
            Assert.IsTrue(File.Exists(manual));
        }

        [TestMethod]
        public void TryFindByIdentity_FindsOwnedEntry()
        {
            var path = Path.Combine(_tempRoot, "01m00s_App.lnk");
            File.WriteAllText(path, "owned");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            manifest.Touch(path, identity: "startup:App.lnk");

            var found = manifest.TryFindByIdentity("STARTUP:app.LNK", out var relativePath, out var entry);

            Assert.IsTrue(found);
            Assert.AreEqual("01m00s_App.lnk", relativePath);
            Assert.AreEqual("startup:App.lnk", entry.Identity);
        }

        [TestMethod]
        public void IsIgnoredConflict_MatchesExactFingerprintOnly()
        {
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            var fingerprint = new ConflictFingerprint(
                ManifestPath: "03m10s_XPTO.cmd",
                ManifestDelay: "03m10s",
                FilePath: "02m40s_XPTO.cmd",
                FileDelay: "02m40s",
                OptionsPath: "04m30s_XPTO.cmd",
                OptionsDelay: "04m30s");

            manifest.IgnoreConflict("startup:xpto.cmd", fingerprint);

            Assert.IsTrue(manifest.IsIgnoredConflict("startup:xpto.cmd", fingerprint));
            Assert.IsFalse(manifest.IsIgnoredConflict(
                "startup:xpto.cmd",
                fingerprint with { OptionsDelay = "05m00s" }));
        }

        [TestMethod]
        public void ClearIgnoredConflicts_RemovesAllEntriesForIdentity()
        {
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            var fingerprint = new ConflictFingerprint(FilePath: "02m40s_XPTO.cmd");
            manifest.IgnoreConflict("startup:xpto.cmd", fingerprint);

            manifest.ClearIgnoredConflicts("startup:xpto.cmd");

            Assert.IsFalse(manifest.IsIgnoredConflict("startup:xpto.cmd", fingerprint));
        }

        [TestMethod]
        public void CanWriteOrHandleConflict_SuppressesExactIgnoredOutputConflict()
        {
            var path = Path.Combine(_tempRoot, "manual.cmd");
            File.WriteAllText(path, "manual");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            var identity = manifest.GetOutputIdentity(path);
            var fingerprint = new ConflictFingerprint(FilePath: "manual.cmd", OptionsPath: "manual.cmd");
            manifest.IgnoreConflict(identity, fingerprint);

            Assert.IsFalse(manifest.CanWriteOrHandleConflict(path));
            Assert.IsTrue(manifest.IsIgnoredConflict(identity, fingerprint));
        }

        [TestMethod]
        public void CanWriteOrHandleConflict_InvalidatesDifferentOutputConflict()
        {
            var path = Path.Combine(_tempRoot, "manual.cmd");
            File.WriteAllText(path, "manual");
            var manifest = OwnedOutputManifest.Load(_tempRoot, "manifest.json", "test");
            var identity = manifest.GetOutputIdentity(path);
            manifest.IgnoreConflict(identity, new ConflictFingerprint(FilePath: "manual.cmd", OptionsPath: "old.cmd"));

            Assert.IsFalse(manifest.CanWriteOrHandleConflict(path));
            Assert.IsFalse(manifest.File.IgnoredConflicts.ContainsKey(identity));
        }
    }
}
