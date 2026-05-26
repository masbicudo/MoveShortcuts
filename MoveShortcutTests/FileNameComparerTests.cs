using MoveShortcuts;

namespace MoveShortcutTests
{
    [TestClass]
    public class FileNameComparerTests
    {
        [TestMethod]
        public void Sort_OrdersVersionLikeNamesNumerically()
        {
            List<string> strings = new() { "v2.0", "v10", "v1.0.9" };

            strings.Sort(FileNameComparer.Default);

            CollectionAssert.AreEqual(new[] { "v1.0.9", "v2.0", "v10" }, strings);
        }
    }
}
