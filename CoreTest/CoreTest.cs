using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreTest
{
    [TestClass]
    public class CoreTest
    {
        static System.Security.Cryptography.SHA1 Hasher = HashTools.GetSHA1Hasher();

        private MetadataNode CreateBasicVirtualFS()
        {
            MetadataNode vfsroot = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("src"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("dst"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("cache"));
            return vfsroot;
        }

        private (BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths) AddStandardVFSFiles(
            MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore, string srcpath = "c/src")
        {
            BPlusTree<byte[]> verifydatastore = new BPlusTree<byte[]>(10);
            Dictionary<string, byte[]> verifyfilepaths = new Dictionary<string, byte[]>();

            (byte[] hash, byte[] file) = MakeRandomFile(100_000_000); // 100 MB file
            AddFileToVFS(Path.Combine("c", "src", "big"), hash, file);

            (hash, file) = MakeRandomFile(0); // Empty file
            AddFileToVFS(Path.Combine("c", "src", "empty"), hash, file);

            (hash, file) = MakeRandomFile(1); // 1byte file
            AddFileToVFS(Path.Combine("c", "src", "1b"), hash, file);
            
            (hash, file) = MakeRandomFile(2); // 2byte file
            AddFileToVFS(Path.Combine("c", "src", "2b"), hash, file);

            foreach (var num in Enumerable.Range(0, 200))
            {
                (hash, file) = MakeRandomFile(55_000); // regular size file
                AddFileToVFS(Path.Combine("c", "src", String.Format("reg_{0}", num)), hash, file);
            }

            return (verifydatastore, verifyfilepaths);

            void AddFileToVFS(string path, byte[] filehash, byte[] filedata)
            {
                verifydatastore.AddHash(filehash, filedata);
                verifyfilepaths[path] = filehash;
                vfsdatastore.AddHash(filehash, filedata);
                vfsroot.AddFile(VirtualFSInterop.MakeNewFileMetadata(Path.GetFileName(path), filehash));
            }
        }

        static (byte[] hash, byte[] file) MakeRandomFile(int size)
        {
            byte[] data = new byte[size];
            Random rng = new Random();
            rng.NextBytes(data);
            return (Hasher.ComputeHash(data), data);
        }

        [TestMethod]
        public void TestInitializeNew()
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> datastore = new BPlusTree<byte[]>(10);
            ICoreDependencies dependencies = new FSCoreDependencies(new VirtualFSInterop(vfsroot, datastore), "src",
                "dst", "cache");
            Core.InitializeNew(dependencies, "test");

            var dst = vfsroot.GetDirectory("dst");
            Assert.IsTrue(dst.HasDirectory(FSCoreDependencies.BlobDirName));
            Assert.IsTrue(dst.HasDirectory(FSCoreDependencies.IndexDirName));
            var idx = dst.GetDirectory(FSCoreDependencies.IndexDirName);
            Assert.IsTrue(idx.HasDirectory(FSCoreDependencies.BackupStoreDirName));
            Assert.IsTrue(idx.Files.ContainsKey(FSCoreDependencies.BlobStoreIndexFilename));
            var bss = idx.GetDirectory(FSCoreDependencies.BackupStoreDirName);
            Assert.IsTrue(bss.Files.ContainsKey("test"));
        }

        [TestMethod]
        public void TestLoadCore_NewlyInitialized()
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> datastore = new BPlusTree<byte[]>(10);
            ICoreDependencies dependencies = new FSCoreDependencies(new VirtualFSInterop(vfsroot, datastore), "src",
                "dst", "cache");
            Core.InitializeNew(dependencies, "test");
            Core.LoadCore(new FSCoreDependencies(new VirtualFSInterop(vfsroot, datastore), "src",
                "dst", "cache"));
        }
        
        [TestMethod]
        public void TestPathMatchesPattern()
        {
            string path1 = "a/b/c/d/efg/h.i";
            string pattern1 = "*c*g*";
            Assert.IsTrue(Core.PatternMatchesPath(path1, pattern1));
        }

        [TestMethod]
        public void TestCheckTrackFile()
        {
            List<(int, string)> patterns = new List<(int, string)>
            {
                (2, "*"),
                (3, "*/cats/*"),
                (0, "*.jpeg"),
                (1, "*/dogs/*.jpeg")
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
            List<(int, string)> patterns = new List<(int, string)>
            {
                (2, "*"),
                (1, "*/cats"),
                (0, "*.jpeg"),
                (3, "*/dogs/*.jpeg"),
                (0, "/dogs*")
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
                var res = Core.CheckTrackAnyDirectoryChild(directories[i], patterns);
                Assert.AreEqual(res, correctoutput[i]);
            }
        }
    }
}
