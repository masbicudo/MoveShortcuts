using ProgramStarter;
using System.Diagnostics;

namespace MoveShortcutTests
{
    [TestClass]
    public class ProgramStarterParserTests
    {
        [TestMethod]
        public void TryParseDelay_ParsesMinutesAndSeconds()
        {
            var parsed = StartupEntryParser.TryParseDelay("01m30s", out var delay);

            Assert.IsTrue(parsed);
            Assert.AreEqual(TimeSpan.FromSeconds(90), delay);
        }

        [TestMethod]
        public void TryParseDelay_ParsesSecondsOnly()
        {
            var parsed = StartupEntryParser.TryParseDelay("45s", out var delay);

            Assert.IsTrue(parsed);
            Assert.AreEqual(TimeSpan.FromSeconds(45), delay);
        }

        [TestMethod]
        public void TryParse_UsesFilenameDelayForManualFile()
        {
            var parsed = StartupEntryParser.TryParse(
                @"C:\Shortcuts\ProgramStarter\01m05s_App.lnk",
                manifest: null,
                out var entry,
                out var reason);

            Assert.IsTrue(parsed, reason);
            Assert.AreEqual(TimeSpan.FromSeconds(65), entry.Delay);
            Assert.AreEqual("App", entry.DisplayName);
        }

        [TestMethod]
        public void TryParse_UsesManifestDelayAndWindowForManagedFile()
        {
            var manifest = new ProgramStarterManifest
            {
                Entries =
                {
                    ["Managed.lnk"] = new ProgramStarterManifestEntry
                    {
                        Delay = "02m00s",
                        Window = "minimized",
                    }
                }
            };

            var parsed = StartupEntryParser.TryParse(
                @"C:\Shortcuts\ProgramStarter\Managed.lnk",
                manifest,
                out var entry,
                out var reason);

            Assert.IsTrue(parsed, reason);
            Assert.AreEqual(TimeSpan.FromMinutes(2), entry.Delay);
            Assert.AreEqual(ProcessWindowStyle.Minimized, entry.WindowStyle);
        }

        [TestMethod]
        public void TryParse_RejectsFileWithoutDelayOrManifestEntry()
        {
            var parsed = StartupEntryParser.TryParse(
                @"C:\Shortcuts\ProgramStarter\App.lnk",
                manifest: null,
                out _,
                out var reason);

            Assert.IsFalse(parsed);
            StringAssert.Contains(reason, "delay");
        }
    }
}
