using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class FileOptionMatcherTests
    {
        [TestMethod]
        public void TryGetOptions_UsesCaseInsensitiveExactMatch()
        {
            var options = new Dictionary<string, MyFileOptions>
            {
                ["Calculator"] = new()
            };
            var matcher = new FileOptionMatcher(options, includeFullyQualifiedKeys: false);

            var matched = matcher.TryGetOptions("calculator", out var option);

            Assert.IsTrue(matched);
            Assert.AreSame(options["Calculator"], option);
        }

        [TestMethod]
        public void TryGetOptions_UsesRegexFallbackForPatternKeys()
        {
            var options = new Dictionary<string, MyFileOptions>
            {
                ["Google .*"] = new()
            };
            var matcher = new FileOptionMatcher(options, includeFullyQualifiedKeys: false);

            var matched = matcher.TryGetOptions("Google Calendar", out var option);

            Assert.IsTrue(matched);
            Assert.AreSame(options["Google .*"], option);
        }

        [TestMethod]
        public void TryGetOptions_DoesNotTreatPlainLiteralAsPattern()
        {
            var options = new Dictionary<string, MyFileOptions>
            {
                ["Google Calendar"] = new()
            };
            var matcher = new FileOptionMatcher(options, includeFullyQualifiedKeys: false);

            var matched = matcher.TryGetOptions("Google Calendar Beta", out _);

            Assert.IsFalse(matched);
        }

        [TestMethod]
        public void TryGetOptions_DoesNotUseFullyQualifiedKeysAsPatternsWhenExcluded()
        {
            var fullPath = Path.Combine(Path.GetTempPath(), "Google .*");
            var options = new Dictionary<string, MyFileOptions>
            {
                [fullPath] = new()
            };
            var matcher = new FileOptionMatcher(options, includeFullyQualifiedKeys: false);

            var matched = matcher.TryGetOptions("Google Calendar", out _);

            Assert.IsFalse(matched);
        }
    }
}
