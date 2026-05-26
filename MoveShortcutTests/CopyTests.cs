using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class CopyTests
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
        public void Copy_PreservesCreationAndLastWriteTimes()
        {
            var source = Path.Combine(_tempRoot, "source.lnk");
            var target = Path.Combine(_tempRoot, "target.lnk");
            File.WriteAllText(source, "shortcut");
            var creationTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var lastWriteTime = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetCreationTimeUtc(source, creationTime);
            File.SetLastWriteTimeUtc(source, lastWriteTime);

            Helpers.Copy(source, target);

            Assert.AreEqual(creationTime, File.GetCreationTimeUtc(target));
            Assert.AreEqual(lastWriteTime, File.GetLastWriteTimeUtc(target));
        }

        [TestMethod]
        public void Copy_SkipsTargetWhenMetadataMatches()
        {
            var source = Path.Combine(_tempRoot, "source.lnk");
            var target = Path.Combine(_tempRoot, "target.lnk");
            File.WriteAllText(source, "shortcut");
            File.WriteAllText(target, "shortcut");
            var creationTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var lastWriteTime = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetCreationTimeUtc(source, creationTime);
            File.SetLastWriteTimeUtc(source, lastWriteTime);
            File.SetCreationTimeUtc(target, creationTime);
            File.SetLastWriteTimeUtc(target, lastWriteTime);
            var targetWriteTimeBefore = File.GetLastWriteTimeUtc(target);

            Helpers.Copy(source, target);

            Assert.AreEqual(targetWriteTimeBefore, File.GetLastWriteTimeUtc(target));
        }

        [TestMethod]
        public void Copy_OverwritesTargetWhenLengthDiffers()
        {
            var source = Path.Combine(_tempRoot, "source.lnk");
            var target = Path.Combine(_tempRoot, "target.lnk");
            File.WriteAllText(source, "new shortcut");
            File.WriteAllText(target, "old");
            var creationTime = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            var lastWriteTime = new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetCreationTimeUtc(source, creationTime);
            File.SetLastWriteTimeUtc(source, lastWriteTime);

            Helpers.Copy(source, target);

            Assert.AreEqual("new shortcut", File.ReadAllText(target));
            Assert.AreEqual(creationTime, File.GetCreationTimeUtc(target));
            Assert.AreEqual(lastWriteTime, File.GetLastWriteTimeUtc(target));
        }
    }
}
