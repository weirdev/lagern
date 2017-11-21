﻿using System;
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

        public static MetadataNode CreateBasicVirtualFS()
        {
            MetadataNode vfsroot = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("src"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("dst"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("cache"));
            return vfsroot;
        }

        private static (BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths) AddStandardVFSFiles(
            MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore)
        {
            BPlusTree<byte[]> verifydatastore = new BPlusTree<byte[]>(10);
            Dictionary<string, byte[]> verifyfilepaths = new Dictionary<string, byte[]>();

            (byte[] hash, byte[] file) = MakeRandomFile(10_000_000); // 10 MB file
            AddFileToVFS(Path.Combine("src", "big"), hash, file);

            (hash, file) = MakeRandomFile(0); // Empty file
            AddFileToVFS(Path.Combine("src", "empty"), hash, file);

            (hash, file) = MakeRandomFile(1); // 1byte file
            AddFileToVFS(Path.Combine("src", "1b"), hash, file);
            
            (hash, file) = MakeRandomFile(2); // 2byte file
            AddFileToVFS(Path.Combine("src", "2b"), hash, file);
            
            foreach (var num in Enumerable.Range(0, 200))
            {
                (hash, file) = MakeRandomFile(55_000); // regular size file
                AddFileToVFS(Path.Combine("src", String.Format("reg_{0}", num)), hash, file);
            }

            return (verifydatastore, verifyfilepaths);

            void AddFileToVFS(string path, byte[] filehash, byte[] filedata)
            {
                verifydatastore.AddOrFind(filehash, filedata);
                verifyfilepaths[path] = filehash;
                vfsdatastore.AddOrFind(filehash, filedata);
                vfsroot.AddFile(Path.GetDirectoryName(path), 
                    VirtualFSInterop.MakeNewFileMetadata(Path.GetFileName(path), filedata.Length, filehash));
            }
        }

        public static (Core core, BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths,
            MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) InitializeNewCoreWithStandardFiles()
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> vfsdatastore = new BPlusTree<byte[]>(10);
            (BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths) = AddStandardVFSFiles(vfsroot, vfsdatastore);
            ICoreDependencies dependencies = new FSCoreDependencies(new VirtualFSInterop(vfsroot, vfsdatastore), "src",
                "dst", "cache");
            Core core = Core.InitializeNew(dependencies, "test");
            return (core, verifydatastore, verifyfilepaths, vfsroot, vfsdatastore);
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
        public void TestRunBackup()
        {
            var testdata = InitializeNewCoreWithStandardFiles();

            testdata.core.RunBackup("test", "run1");
            testdata.vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            testdata.core.RunBackup("test", "run2");
        }

        [TestMethod]
        public void TestGetWTStatus()
        {
            var testdata = InitializeNewCoreWithStandardFiles();

            testdata.core.GetWTStatus("test");
        }

        [TestMethod]
        public void TestRestore()
        {
            var testdata = InitializeNewCoreWithStandardFiles();
            testdata.core.RunBackup("test", "run1");

            testdata.core.RestoreFileOrDirectory("test", "2b", "2b", null, true);
            Assert.IsTrue(testdata.vfsroot.Files.ContainsKey("2b"));
        }

        [TestMethod]
        public void TestRemoveBackup()
        {
            var testdata = InitializeNewCoreWithStandardFiles();

            var bh1 = testdata.core.RunBackup("test", "run1");
            testdata.vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            var bh2 = testdata.core.RunBackup("test", "run2");
            testdata.core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh1));
            testdata.core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh2).Substring(0, 10));
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
