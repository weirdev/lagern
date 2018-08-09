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

        public static MetadataNode CreateBasicVirtualFS()
        {
            MetadataNode vfsroot = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("src"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("dst"));
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("cache"));
            return vfsroot;
        }

        private static (BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths) AddStandardVFSFiles(
            MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore, int? randomseed=null)
        {
            BPlusTree<byte[]> verifydatastore = new BPlusTree<byte[]>(10);
            Dictionary<string, byte[]> verifyfilepaths = new Dictionary<string, byte[]>();
            
            (byte[] hash, byte[] file) = MakeRandomFile(10_000_000, randomseed); // 10 MB file
            AddFileToVFS(Path.Combine("src", "big"), hash, file);

            (hash, file) = MakeRandomFile(0); // Empty file
            AddFileToVFS(Path.Combine("src", "empty"), hash, file);

            (hash, file) = MakeRandomFile(1, randomseed - 1); // 1byte file
            AddFileToVFS(Path.Combine("src", "1b"), hash, file);
            
            (hash, file) = MakeRandomFile(2, randomseed - 2); // 2byte file
            AddFileToVFS(Path.Combine("src", "2b"), hash, file);
            
            foreach (var num in Enumerable.Range(0, 100))
            {
                (hash, file) = MakeRandomFile(55_000, randomseed + num); // regular size file
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
            MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) InitializeNewCoreWithStandardFiles(int? randomseed=null, 
            bool encrypted=false)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> vfsdatastore = new BPlusTree<byte[]>(10);
            (BPlusTree<byte[]> verifydatastore, Dictionary<string, byte[]> verifyfilepaths) = AddStandardVFSFiles(vfsroot, vfsdatastore, randomseed);
            var vfsisrc = new VirtualFSInterop(vfsroot, vfsdatastore);
            IDstFSInterop vfsidst;
            if (encrypted)
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, "dst", "password");
            }
            else
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, "dst");
            }
            var vfsicache = VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc, "dst", "cache");
            ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", vfsidst, true);
            ICoreDstDependencies cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
            Core core = new Core(srcdeps, dstdeps, cachedeps);
            return (core, verifydatastore, verifyfilepaths, vfsroot, vfsdatastore);
        }

        static (byte[] hash, byte[] file) MakeRandomFile(int size, int? seed=null)
        {
            byte[] data = new byte[size];
            Random rng;
            if (seed==null)
            {
                rng = new Random();
            }
            else
            {
                rng = new Random(seed.Value);
            }
            rng.NextBytes(data);
            return (Hasher.ComputeHash(data), data);
        }

        public void InitializeNew(bool encrypted)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> datastore = new BPlusTree<byte[]>(10);
            var vfsisrc = new VirtualFSInterop(vfsroot, datastore);
            IDstFSInterop vfsidst;
            if (encrypted)
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "dst", "password");
            }
            else
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "dst");
            }
            var vfsicache = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc);
            ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", vfsidst, true);
            ICoreDstDependencies cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
            Core core = new Core(srcdeps, dstdeps, cachedeps);
        }

        [TestMethod]
        public void TestInitializeNew()
        {
            InitializeNew(false);
        }

        [TestMethod]
        public void TestInitializeNew_Encrypted()
        {
            InitializeNew(true);
        }

        public void LoadCore_NewlyInitialized(bool encrypted)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS();
            BPlusTree<byte[]> datastore = new BPlusTree<byte[]>(10);
            var vfsisrc = new VirtualFSInterop(vfsroot, datastore);
            IDstFSInterop vfsidst;
            if (encrypted)
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "dst", "password");
            } else
            {
                vfsidst = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "dst");
            }
            var vfsicache = VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc, "dst", "cache");
            ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", vfsidst, true);
            ICoreDstDependencies cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
            Core core = new Core(srcdeps, dstdeps, cachedeps);

            vfsisrc = new VirtualFSInterop(vfsroot, datastore);
            if (encrypted)
            {
                vfsidst = VirtualFSInterop.LoadDst(vfsroot, datastore, "dst", "password");
            }
            else
            {
                vfsidst = VirtualFSInterop.LoadDst(vfsroot, datastore, "dst");
            }
            vfsicache = VirtualFSInterop.LoadDst(vfsroot, datastore, "cache");
            srcdeps = FSCoreSrcDependencies.Load("src", vfsisrc);
            dstdeps = CoreDstDependencies.Load(vfsidst, true);
            cachedeps = CoreDstDependencies.Load(vfsicache, false);
            core = new Core(srcdeps, dstdeps, cachedeps);
        }

        [TestMethod]
        public void TestLoadCore_NewlyInitialized()
        {
            LoadCore_NewlyInitialized(false);
        }

        [TestMethod]
        public void TestLoadCore_NewlyInitialized_Encrypted()
        {
            LoadCore_NewlyInitialized(true);
        }

        public void RunBackup(bool encrypted)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted);

            testdata.core.RunBackup("test", "run1");
            testdata.vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            testdata.core.RunBackup("test", "run2");
        }

        [TestMethod]
        public void TestRunBackup()
        {
            RunBackup(false);
        }

        [TestMethod]
        public void TestRunBackup_Encrypted()
        {
            RunBackup(true);
        }

        public void GetWTStatus(bool encrypted)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted);

            testdata.core.GetWTStatus("test");
        }

        [TestMethod]
        public void TestGetWTStatus()
        {
            GetWTStatus(false);
        }

        [TestMethod]
        public void TestGetWTStatus_Encrypted()
        {
            GetWTStatus(true);
        }

        public void Restore(bool encrypted)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted);
            testdata.core.RunBackup("test", "run1");

            testdata.core.RestoreFileOrDirectory("test", "2b", "2b", null, true);
            Assert.IsTrue(testdata.vfsroot.Files.ContainsKey("2b"));
            // TODO: Check data here as well
        }

        [TestMethod]
        public void TestRestore()
        {
            Restore(false);
        }

        [TestMethod]
        public void TestRestore_Encrypted()
        {
            Restore(false);
        }

        public void RemoveBackup(bool encrypted)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted);

            var bh1 = testdata.core.RunBackup("test", "run1");
            System.Threading.Thread.Sleep(500); // Allow async writes to finish

            testdata.vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            var bh2 = testdata.core.RunBackup("test", "run2");
            testdata.core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh1));
            testdata.core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh2).Substring(0, 10));
        }

        [TestMethod]
        public void TestRemoveBackup()
        {
            RemoveBackup(false);
        }

        [TestMethod]
        public void TestRemoveBackup_Encrypted()
        {
            RemoveBackup(true);
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

        public static void RandomData(byte[] data)
        {
            Random rng = new Random();
            rng.NextBytes(data);
        }
    }
}
