using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class SelfReferenceBuilderTests
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
        public void CreateSelfReferenceOptions_UsesDotnetHost_ForDllAssembly()
        {
            var assembly = Path.Combine(_tempRoot, "MoveShortcuts.dll");
            var dotnet = Path.Combine(_tempRoot, "dotnet.exe");
            File.WriteAllText(assembly, "");
            File.WriteAllText(dotnet, "");

            var options = SelfReferenceBuilder.CreateSelfReferenceOptions("mvshct", "mvshct-edit", assembly, dotnet);

            Assert.AreEqual(dotnet, options["mvshct"].Target);
            StringAssert.Contains(options["mvshct"].Arguments, "MoveShortcuts.dll");
            CollectionAssert.AreEqual(new[] { "cmd" }, options["mvshct"].LinkTypes);
            StringAssert.Contains(options["mvshct-edit"].Arguments, " edit");
        }

        [TestMethod]
        public void CreateSelfReferenceOptions_UsesExecutableDirectly_ForExeAssembly()
        {
            var assembly = Path.Combine(_tempRoot, "MoveShortcuts.exe");
            File.WriteAllText(assembly, "");

            var options = SelfReferenceBuilder.CreateSelfReferenceOptions("mvshct", "mvshct-edit", assembly, null);

            Assert.AreEqual(assembly, options["mvshct"].Target);
            Assert.IsNull(options["mvshct"].Arguments);
            Assert.AreEqual("edit", options["mvshct-edit"].Arguments);
        }
    }
}
