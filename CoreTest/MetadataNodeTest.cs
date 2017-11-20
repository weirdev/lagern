using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.IO;

namespace CoreTest
{
    [TestClass]
    public class MetadataNodeTest
    {
        
        [TestMethod]
        public void TestGetFile()
        {
            MetadataNode mtree = CoreTest.CreateBasicVirtualFS();
            var a = VirtualFSInterop.MakeNewFileMetadata("a.ext", 100);
            mtree.AddFile(a);
            var b = VirtualFSInterop.MakeNewFileMetadata("b");
            mtree.AddFile("src", b);
            mtree.AddDirectory("dst", VirtualFSInterop.MakeNewDirectoryMetadata("hat"));
            var c = VirtualFSInterop.MakeNewFileMetadata("c.ext");
            mtree.AddFile(Path.Combine("dst", "hat"), c);

            Assert.AreSame(mtree.GetFile("a.ext"), a);
            Assert.AreSame(mtree.GetFile(Path.Combine("src", "b")), b);
            Assert.AreSame(mtree.GetFile(Path.Combine("dst", "hat", "c.ext")), c);
        }
    }
}
