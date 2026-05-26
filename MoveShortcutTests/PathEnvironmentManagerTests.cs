using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class PathEnvironmentManagerTests
    {
        [TestMethod]
        public void IsDirectoryInPath_ReturnsTrue_ForEquivalentPathWithTrailingSlash()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MoveShortcutsPathTest");
            var pathValue = directory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            Assert.IsTrue(PathEnvironmentManager.IsDirectoryInPath(directory, pathValue));
        }

        [TestMethod]
        public void IsDirectoryInPath_ReturnsFalse_WhenDirectoryIsMissing()
        {
            var directory = Path.Combine(Path.GetTempPath(), "MoveShortcutsPathTest");
            var pathValue = Path.Combine(Path.GetTempPath(), "OtherPathTest");

            Assert.IsFalse(PathEnvironmentManager.IsDirectoryInPath(directory, pathValue));
        }

        [TestMethod]
        public void AppendPathEntry_AppendsUsingPathSeparator()
        {
            var updated = PathEnvironmentManager.AppendPathEntry(@"C:\Tools", @"C:\Shortcuts");

            Assert.AreEqual(@"C:\Tools;C:\Shortcuts", updated);
        }

        [TestMethod]
        public void BuildPathValue_LeavesExistingEntryInPlace_ByDefault()
        {
            var updated = PathEnvironmentManager.BuildPathValue(
                @"C:\Tools;C:\Shortcuts;C:\Other",
                @"C:\Shortcuts",
                PathPlacement.AppendIfMissing);

            Assert.AreEqual(@"C:\Tools;C:\Shortcuts;C:\Other", updated);
        }

        [TestMethod]
        public void BuildPathValue_AppendsMissingEntry_ByDefault()
        {
            var updated = PathEnvironmentManager.BuildPathValue(
                @"C:\Tools;C:\Other",
                @"C:\Shortcuts",
                PathPlacement.AppendIfMissing);

            Assert.AreEqual(@"C:\Tools;C:\Other;C:\Shortcuts", updated);
        }

        [TestMethod]
        public void BuildPathValue_MovesExistingEntryToFront_WhenPriorityIsRequested()
        {
            var updated = PathEnvironmentManager.BuildPathValue(
                @"C:\Tools;C:\Shortcuts;C:\Other",
                @"C:\Shortcuts",
                PathPlacement.PrependOrMove);

            Assert.AreEqual(@"C:\Shortcuts;C:\Tools;C:\Other", updated);
        }

        [TestMethod]
        public void BuildPathValue_MovesExistingEntryToEnd_WhenLastIsRequested()
        {
            var updated = PathEnvironmentManager.BuildPathValue(
                @"C:\Tools;C:\Shortcuts;C:\Other",
                @"C:\Shortcuts",
                PathPlacement.AppendOrMove);

            Assert.AreEqual(@"C:\Tools;C:\Other;C:\Shortcuts", updated);
        }

        [TestMethod]
        public void AddToUserPath_ReturnsAlreadyPresent_WhenPathAlreadyContainsDirectory()
        {
            var original = Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User);
            try
            {
                Environment.SetEnvironmentVariable("Path", @"C:\Tools;C:\Shortcuts", EnvironmentVariableTarget.User);

                var status = PathEnvironmentManager.AddToUserPath(@"C:\Shortcuts");

                Assert.AreEqual(PathUpdateStatus.AlreadyPresent, status);
                Assert.AreEqual(@"C:\Tools;C:\Shortcuts", Environment.GetEnvironmentVariable("Path", EnvironmentVariableTarget.User));
            }
            finally
            {
                Environment.SetEnvironmentVariable("Path", original, EnvironmentVariableTarget.User);
            }
        }
    }
}
