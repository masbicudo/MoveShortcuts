using MoveShortcuts;
using Newtonsoft.Json;

namespace MoveShortcutTests
{
    [TestClass]
    public class ResolvedTargetCacheTests
    {
        private string _tempRoot = "";
        private string _cacheFile = "";

        [TestInitialize]
        public void SetUp()
        {
            ResolvedTargetCacheProvider.ResetForTests();
            _tempRoot = Path.Combine(Path.GetTempPath(), "MoveShortcutsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _cacheFile = Path.Combine(_tempRoot, "target-cache.json");
        }

        [TestCleanup]
        public void TearDown()
        {
            ResolvedTargetCacheProvider.ResetForTests();
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [TestMethod]
        public void Resolve_UsesCachedTarget_WhenTargetStillExists()
        {
            var target = Path.Combine(_tempRoot, "tool.exe");
            File.WriteAllText(target, "");
            WriteCache("es", "tool.exe", target);

            var calls = 0;
            var resolved = ResolvedTargetCacheProvider.Resolve(
                _cacheFile,
                "es",
                "tool.exe",
                (_, _) =>
                {
                    calls++;
                    return null;
                });

            Assert.AreEqual(target, resolved);
            Assert.AreEqual(0, calls);
        }

        [TestMethod]
        public void Resolve_CallsResolver_WhenCachedTargetIsMissing()
        {
            var oldTarget = Path.Combine(_tempRoot, "old.exe");
            var newTarget = Path.Combine(_tempRoot, "new.exe");
            File.WriteAllText(newTarget, "");
            WriteCache("es", "tool.exe", oldTarget);

            var calls = 0;
            var resolved = ResolvedTargetCacheProvider.Resolve(
                _cacheFile,
                "es",
                "tool.exe",
                (_, _) =>
                {
                    calls++;
                    return newTarget;
                });

            Assert.AreEqual(newTarget, resolved);
            Assert.AreEqual(1, calls);
        }

        [TestMethod]
        public void Resolve_WritesResolvedTarget()
        {
            var target = Path.Combine(_tempRoot, "tool.exe");
            File.WriteAllText(target, "");

            var resolved = ResolvedTargetCacheProvider.Resolve(
                _cacheFile,
                "es",
                "tool.exe",
                (_, _) => target);

            ResolvedTargetCacheProvider.ResetForTests();
            var resolvedFromCache = ResolvedTargetCacheProvider.Resolve(
                _cacheFile,
                "es",
                "tool.exe",
                (_, _) => throw new InvalidOperationException("resolver should not be called"));

            Assert.AreEqual(target, resolved);
            Assert.AreEqual(target, resolvedFromCache);
        }

        private void WriteCache(string command, string argument, string target)
        {
            var cache = new ResolvedTargetCacheFile();
            cache.entries[ResolvedTargetCacheEntry.CreateKey(command, argument)] = new ResolvedTargetCacheEntry
            {
                command = command,
                argument = argument,
                target = target,
                resolvedUtc = DateTimeOffset.UtcNow,
            };

            File.WriteAllText(_cacheFile, JsonConvert.SerializeObject(cache));
            ResolvedTargetCacheProvider.ResetForTests();
        }
    }
}
