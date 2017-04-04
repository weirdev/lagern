﻿using System;
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
            string[] patterns = new string[]
            {
                "*",
                "*/cats",
                "^*.jpeg",
                "*/dogs/*.jpeg"
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

            bool[] correctoutput = new bool[] { false, false, false, false, true, true, true, true };
            for (int i = 0; i < files.Length; i++)
            {
                Assert.AreEqual(Core.CheckTrackFile(files[i], patterns), correctoutput[i]);
            }
        }
        
        [TestMethod]
        public void TestCheckTrackAnyDirectoryChild()
        {
            string[] patterns = new string[]
            {
                "*",
                "*/cats",
                "^*.jpeg",
                "*/dogs/*.jpeg",
                "^/dogs*"
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
