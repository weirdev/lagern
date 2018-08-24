using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoreTest
{
    // Not currently testing every combination of configurations
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
            bool encrypted=false, bool cache=true)
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
            ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", vfsidst, cache);
            ICoreDstDependencies cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
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

        public void InitializeNew(bool encrypted, bool cache)
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
            if (cache)
            {
                ICoreDstDependencies cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
                Core core = new Core(srcdeps, dstdeps, cachedeps);
            }
            else
            {
                Core core = new Core(srcdeps, dstdeps);
            }
        }

        [TestMethod]
        public void TestInitializeNew()
        {
            InitializeNew(false, true);
            InitializeNew(true, false);
        }

        public void LoadCore_NewlyInitialized(bool encrypted, bool cache)
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
            ICoreDstDependencies cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.InitializeNew("test~cache", vfsicache, false);
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
            cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.Load(vfsicache, false);
            core = new Core(srcdeps, dstdeps, cachedeps);
        }

        [TestMethod]
        public void TestLoadCore_NewlyInitialized()
        {
            LoadCore_NewlyInitialized(false, false);
            LoadCore_NewlyInitialized(true, true);
        }

        public void RunBackup(bool encrypted, bool cache)
        {
            var (core, verifydatastore, verifyfilepaths, vfsroot, vfsdatastore) = 
                InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);

            core.RunBackup("test", "run1");
            vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            System.Threading.Thread.Sleep(40); // Allow async writes to finish
            core.RunBackup("test", "run2");
        }

        [TestMethod]
        public void TestRunBackup()
        {
            RunBackup(false, true);
            RunBackup(true, false);
        }

        public void GetWTStatus(bool encrypted, bool cache)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);

            testdata.core.GetWTStatus("test");
            // TODO: test output
        }

        [TestMethod]
        public void TestGetWTStatus()
        {
            GetWTStatus(false, false);
            GetWTStatus(true, true);
        }

        public void Restore(bool encrypted, bool cache)
        {
            var testdata = InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);
            testdata.core.RunBackup("test", "run1");
            System.Threading.Thread.Sleep(40); // Allow async writes to finish

            testdata.core.RestoreFileOrDirectory("test", "2b", "2b", null, true);
            System.Threading.Thread.Sleep(40); // Allow async writes to finish
            Assert.IsTrue(testdata.vfsroot.Files.ContainsKey("2b"));
            // TODO: Check data match here as well
        }
        
        [TestMethod]
        public void TestRestore()
        {
            Restore(false, true);
            Restore(true, false);
        }

        public void RemoveBackup(bool encrypted, bool cache)
        {
            var (core, verifydatastore, verifyfilepaths, vfsroot, vfsdatastore) = InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);

            var bh1 = core.RunBackup("test", "run1");
            System.Threading.Thread.Sleep(40); // Allow async writes to finish

            vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            var bh2 = core.RunBackup("test", "run2");
            System.Threading.Thread.Sleep(40); // Allow async writes to finish

            // Full hash test
            core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh1));
            // Just prefix
            core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh2).Substring(0, 10));
            // All backups deleted
            Assert.AreEqual(core.GetBackups("test").backups.Count(), 0);
        }

        [TestMethod]
        public void TestRemoveBackup()
        {
            RemoveBackup(false, false);
            RemoveBackup(true, true);
        }

        public void TransferBackupSet(bool encrypted, bool cache)
        {
            var (srcCore, verifydatastore, verifyfilepaths, vfsroot, vfsdatastore) = InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);
            var (dstCore, _, _, _, _) = InitializeNewCoreWithStandardFiles(encrypted: encrypted, cache: cache);

            var bh1 = srcCore.RunBackup("test", "run1");
            System.Threading.Thread.Sleep(40); // Allow async writes to finish

            srcCore.TransferBackupSet("test", dstCore, true);
        }
        
        [TestMethod]
        public void TestTransferBackupSet()
        {
            TransferBackupSet(false, true);
            TransferBackupSet(true, false);
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
