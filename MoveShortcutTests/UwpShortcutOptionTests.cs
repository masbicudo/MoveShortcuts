using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class UwpShortcutOptionTests
    {
        [TestMethod]
        public void HasUwpShortcutOptions_ReturnsFalse_WhenOptionsAreEmpty()
        {
            var options = new Dictionary<string, MyFileOptions>();

            Assert.IsFalse(Helpers.HasUwpShortcutOptions(options));
        }

        [TestMethod]
        public void HasUwpShortcutOptions_ReturnsFalse_WhenAllOptionsAreFullPaths()
        {
            var options = new Dictionary<string, MyFileOptions>
            {
                [Path.Combine(Path.GetTempPath(), "Tool.lnk")] = new()
            };

            Assert.IsFalse(Helpers.HasUwpShortcutOptions(options));
        }

        [TestMethod]
        public void HasUwpShortcutOptions_ReturnsTrue_WhenOptionCanMatchAppName()
        {
            var options = new Dictionary<string, MyFileOptions>
            {
                ["Calculator"] = new()
            };

            Assert.IsTrue(Helpers.HasUwpShortcutOptions(options));
        }
    }
}
