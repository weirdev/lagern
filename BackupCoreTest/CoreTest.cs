using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;

namespace BackupCoreTest
{
    [TestClass]
    public class CoreTest
    {
        [TestMethod]
        public void TestPathMatchesPattern()
        {
            string path1 = "a/b/c/d/efg/h.i";
            string pattern1 = "*c*g*";
            Assert.IsTrue(Core.PathMatchesPattern(path1, pattern1));
        }

        [TestMethod]
        public void TestCheckTrackFile()
        {
            Tuple<int, string>[] patterns = new Tuple<int, string>[]
            {
                new Tuple<int, string>(2, "*"),
                new Tuple<int, string>(3, "*/cats/*"),
                new Tuple<int, string>(0, "*.jpeg"),
                new Tuple<int, string>(1, "*/dogs/*.jpeg")
            };

            string[] files = new string[]
            {
                "/ninjas/hello/batman.jpeg",
                "/.jpeg",
                "/af.jpeg",
                "/cats/jj.jpeg",
                "/cats/hhh.txt",
                "/log.txt",
                "/dogs/goodboy.jpeg",
                "/cats/dogs/goodboy.jpeg"
            };

            int[] correctoutput = new int[] { 0, 0, 0, 0, 3, 2, 1, 1 };
            for (int i = 0; i < files.Length; i++)
            {
                int a = Core.FileTrackClass(files[i], patterns);
                Assert.AreEqual(Core.FileTrackClass(files[i], patterns), correctoutput[i]);
            }
        }
        
        [TestMethod]
        public void TestCheckTrackAnyDirectoryChild()
        {
            Tuple<int, string>[] patterns = new Tuple<int, string>[]
            {
                new Tuple<int, string>(2, "*"),
                new Tuple<int, string>(1, "*/cats"),
                new Tuple<int, string>(0, "*.jpeg"),
                new Tuple<int, string>(3, "*/dogs/*.jpeg"),
                new Tuple<int, string>(0, "/dogs*")
            };

            string[] directories = new string[]
            {
                "/ninjas/hello",
                "/af",
                "/cats",
                "/dogs/goodboy",
                "/cats/dogs/goodboy",
                "/dogs"
            };

            bool[] correctoutput = new bool[] { true, true, true, false, true, false };
            for (int i = 0; i < directories.Length; i++)
            {
                Assert.AreEqual(Core.CheckTrackAnyDirectoryChild(directories[i], patterns), correctoutput[i]);
            }
        }
    }
}
