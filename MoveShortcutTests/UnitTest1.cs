using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestFileNameComparer()
        {
            List<string> strings = new() { "v2.0", "v10", "v1.0.9" };
            strings.Sort(FileNameComparer.Default);
            CollectionAssert.AreEqual(strings, new string[] { "v1.0.9", "v2.0", "v10" });
        }
    }
}