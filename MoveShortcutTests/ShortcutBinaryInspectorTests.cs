using System.Text;
using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class ShortcutBinaryInspectorTests
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
        public void ContainsTargetHint_ReturnsTrue_WhenShellLinkContainsAppsFolderId()
        {
            var link = Path.Combine(_tempRoot, "app.lnk");
            var appId = "Contoso.App_abc123!App";
            File.WriteAllBytes(link, CreateShellLinkBytes(appId));

            Assert.IsTrue(ShortcutBinaryInspector.ContainsTargetHint(link, @$"shell:AppsFolder\{appId}"));
        }

        [TestMethod]
        public void ContainsTargetHint_ReturnsFalse_WhenHeaderIsNotShellLink()
        {
            var link = Path.Combine(_tempRoot, "app.lnk");
            File.WriteAllBytes(link, Encoding.Unicode.GetBytes("Contoso.App_abc123!App"));

            Assert.IsFalse(ShortcutBinaryInspector.ContainsTargetHint(link, @"shell:AppsFolder\Contoso.App_abc123!App"));
        }

        [TestMethod]
        public void ContainsTargetHint_ReturnsFalse_WhenHintIsMissing()
        {
            var link = Path.Combine(_tempRoot, "app.lnk");
            File.WriteAllBytes(link, CreateShellLinkBytes("Different.App_abc123!App"));

            Assert.IsFalse(ShortcutBinaryInspector.ContainsTargetHint(link, @"shell:AppsFolder\Contoso.App_abc123!App"));
        }

        private static byte[] CreateShellLinkBytes(string payload)
        {
            var header = new byte[0x4C];
            BitConverter.GetBytes(0x4C).CopyTo(header, 0);
            new byte[]
            {
                0x01, 0x14, 0x02, 0x00,
                0x00, 0x00, 0x00, 0x00,
                0xC0, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x46
            }.CopyTo(header, 4);

            return header.Concat(Encoding.Unicode.GetBytes(payload)).ToArray();
        }
    }
}
