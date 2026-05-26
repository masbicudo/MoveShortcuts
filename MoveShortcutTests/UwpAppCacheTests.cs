using MoveShortcuts;
using Newtonsoft.Json;

namespace MoveShortcutTests
{
    [TestClass]
    public class UwpAppCacheTests
    {
        private string _tempRoot = "";
        private string _cachePath = "";
        private UwpPackageSignature _signature = new();
        private UwpCacheIdentity _identity = new();

        [TestInitialize]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "MoveShortcutsTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
            _cachePath = Path.Combine(_tempRoot, "move-shortcuts-uwp-cache.json");
            _signature = new UwpPackageSignature { packageCount = 2, hash = "sha256:test" };
            _identity = new UwpCacheIdentity
            {
                MachineName = "machine",
                UserName = "user",
                UserSid = "sid",
            };
        }

        [TestCleanup]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }

        [TestMethod]
        public void TryWriteCache_RoundTripsValidCache()
        {
            var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Calculator"] = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"
            };

            UwpAppCacheProvider.TryWriteCache(_cachePath, apps, _signature, _identity);
            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, _signature, _identity);

            Assert.IsNotNull(cache);
            Assert.AreEqual("Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", cache.apps.Single().appUserModelId);
        }

        [TestMethod]
        public void TryReadValidCache_ReturnsNull_WhenSignatureIsStale()
        {
            UwpAppCacheProvider.TryWriteCache(
                _cachePath,
                new Dictionary<string, string>(),
                _signature,
                _identity);
            var newSignature = new UwpPackageSignature { packageCount = 3, hash = "sha256:new" };

            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, newSignature, _identity);

            Assert.IsNull(cache);
        }

        [TestMethod]
        public void TryReadValidCache_ReturnsNull_WhenIdentityDiffers()
        {
            UwpAppCacheProvider.TryWriteCache(
                _cachePath,
                new Dictionary<string, string>(),
                _signature,
                _identity);
            var otherIdentity = new UwpCacheIdentity
            {
                MachineName = _identity.MachineName,
                UserName = _identity.UserName,
                UserSid = "other-sid",
            };

            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, _signature, otherIdentity);

            Assert.IsNull(cache);
        }

        [TestMethod]
        public void TryReadValidCache_ReturnsNull_ForBrokenJson()
        {
            File.WriteAllText(_cachePath, "{ broken json");
            var warnings = new List<string>();

            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, _signature, _identity, warnings.Add);

            Assert.IsNull(cache);
            Assert.AreEqual(1, warnings.Count);
        }

        [TestMethod]
        public void IsCacheValid_ReturnsFalse_ForUnsupportedSchema()
        {
            var cache = new UwpAppCache
            {
                schemaVersion = UwpAppCache.CurrentSchemaVersion + 1,
                machineName = _identity.MachineName,
                userName = _identity.UserName,
                userSid = _identity.UserSid,
                packageSignature = _signature,
            };

            Assert.IsFalse(UwpAppCacheProvider.IsCacheValid(cache, _signature, _identity));
        }

        [TestMethod]
        public void TryReadValidCache_ReturnsNull_WhenCacheIsMissing()
        {
            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, _signature, _identity);

            Assert.IsNull(cache);
        }

        [TestMethod]
        public void CacheJson_UsesExpectedShape()
        {
            UwpAppCacheProvider.TryWriteCache(
                _cachePath,
                new Dictionary<string, string> { ["App"] = "Family!App" },
                _signature,
                _identity);

            var json = File.ReadAllText(_cachePath);
            var cache = JsonConvert.DeserializeObject<UwpAppCache>(json);

            Assert.IsNotNull(cache);
            Assert.AreEqual(UwpAppCache.CurrentSchemaVersion, cache.schemaVersion);
            Assert.AreEqual(_signature.hash, cache.packageSignature.hash);
            Assert.AreEqual("Family!App", cache.apps.Single().appUserModelId);
        }

        [TestMethod]
        public void GetUwpApps_UsesValidCacheWithoutEnumerating()
        {
            var realSignature = UwpAppCacheProvider.CreatePackageSignature();
            var realIdentity = UwpAppCacheProvider.CreateCurrentIdentity();
            UwpAppCacheProvider.TryWriteCache(
                _cachePath,
                new Dictionary<string, string> { ["Cached"] = "Family!Cached" },
                realSignature,
                realIdentity);
            var enumerateCount = 0;

            var apps = UwpAppCacheProvider.GetUwpApps(
                _cachePath,
                refresh: false,
                enumerateApps: () =>
                {
                    enumerateCount++;
                    return new Dictionary<string, string> { ["Fresh"] = "Family!Fresh" };
                });

            Assert.AreEqual(0, enumerateCount);
            Assert.AreEqual("Family!Cached", apps["Cached"]);
            Assert.IsFalse(apps.ContainsKey("Fresh"));
        }

        [TestMethod]
        public void GetUwpApps_RefreshIgnoresValidCache()
        {
            var realSignature = UwpAppCacheProvider.CreatePackageSignature();
            var realIdentity = UwpAppCacheProvider.CreateCurrentIdentity();
            UwpAppCacheProvider.TryWriteCache(
                _cachePath,
                new Dictionary<string, string> { ["Cached"] = "Family!Cached" },
                realSignature,
                realIdentity);
            var enumerateCount = 0;

            var apps = UwpAppCacheProvider.GetUwpApps(
                _cachePath,
                refresh: true,
                enumerateApps: () =>
                {
                    enumerateCount++;
                    return new Dictionary<string, string> { ["Fresh"] = "Family!Fresh" };
                });

            Assert.AreEqual(1, enumerateCount);
            Assert.AreEqual("Family!Fresh", apps["Fresh"]);
            Assert.IsFalse(apps.ContainsKey("Cached"));
        }

        [TestMethod]
        public void TryWriteCache_AllowsNamesThatDifferOnlyByCase()
        {
            var apps = new Dictionary<string, string>
            {
                ["What's new"] = "Family!Lower",
                ["What's New"] = "Family!Upper",
            };

            UwpAppCacheProvider.TryWriteCache(_cachePath, apps, _signature, _identity);
            var cache = UwpAppCacheProvider.TryReadValidCache(_cachePath, _signature, _identity);

            Assert.IsNotNull(cache);
            Assert.AreEqual(2, cache.apps.Count);
            Assert.IsTrue(cache.apps.Any(app => app.name == "What's new" && app.appUserModelId == "Family!Lower"));
            Assert.IsTrue(cache.apps.Any(app => app.name == "What's New" && app.appUserModelId == "Family!Upper"));
        }
    }
}
