﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LagernCore.Models;
using LagernCore.BackupCalculation;
using System.Threading.Tasks;

namespace CoreTest
{
    // Not currently testing every combination of configurations
#pragma warning disable CS0168 // Variable is declared but never used
    [TestClass]
    public class CoreTest
    {
        static readonly System.Security.Cryptography.SHA1 Hasher = HashTools.GetSHA1Hasher();

        public static MetadataNode CreateBasicVirtualFS(int destinations, Random? random = null)
        {
            DateTime dateTime = DateTime.Now;
            if (random != null)
            {
                dateTime = RandomDateTime(random);
            }
            MetadataNode vfsroot = new(VirtualFSInterop.MakeNewDirectoryMetadata("c", dateTime), null);
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("src", dateTime));
            var dstroot = vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("dst", dateTime));
            for (int i = 0; i < destinations; i++)
            {
                dstroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata(i.ToString(), dateTime));
            }
            vfsroot.AddDirectory(VirtualFSInterop.MakeNewDirectoryMetadata("cache", dateTime));
            return vfsroot;
        }

        public static DateTime RandomDateTime(Random? random)
        {
            if (random == null)
            {
                random = new Random();
            }

            DateTime dateTime;
            const int thirtyYearsOfSeconds = 30 * 365 * 24 * 60 * 60;
            int secondsSinceEpoch = random.Next(thirtyYearsOfSeconds);
            dateTime = DateTime.UnixEpoch;
            dateTime.AddSeconds(secondsSinceEpoch);
            return dateTime;
        }

        /// <summary>
        /// Returns a map of file path to the file data
        /// </summary>
        /// <param name="vfsroot"></param>
        /// <param name="vfsdatastore"></param>
        /// <param name="random"></param>
        /// <param name="regSizeFileCount"></param>
        /// <returns></returns>
        public static Dictionary<string, byte[]> AddStandardVFSFiles(
            MetadataNode vfsDirectory, BPlusTree<byte[]> vfsdatastore, Random? random = null, int regSizeFileCount=100)
        {
            Dictionary<string, byte[]> verifyfilepaths = new();
            
            (byte[] hash, byte[] file) = MakeRandomFile(10_000_000, random); // 10 MB file
            AddFileToVFS("big", hash, file);

            (hash, file) = MakeRandomFile(0); // Empty file
            AddFileToVFS("empty", hash, file);

            (hash, file) = MakeRandomFile(1, random); // 1byte file
            AddFileToVFS("1b", hash, file);
            
            (hash, file) = MakeRandomFile(2, random); // 2byte file
            AddFileToVFS("2b", hash, file);
            
            foreach (var num in Enumerable.Range(0, regSizeFileCount))
            {
                (hash, file) = MakeRandomFile(5_000, random); // regular size file
                AddFileToVFS(String.Format("reg_{0}", num), hash, file);
            }

            return verifyfilepaths;

            void AddFileToVFS(string path, byte[] filehash, byte[] filedata)
            {
                new BPlusTree<byte[]>(10).AddOrFind(filehash, filedata);
                verifyfilepaths[path] = filehash;
                vfsdatastore.AddOrFind(filehash, filedata);
                DateTime dateTime = RandomDateTime(random);
                string? dirpath = Path.GetDirectoryName(path);
                if (dirpath == null)
                {
                    throw new NullReferenceException();
                }
                vfsDirectory.AddFile(dirpath, 
                    new FileMetadata(Path.GetFileName(path), dateTime, dateTime, dateTime, FileAttributes.Normal, filedata.Length, filehash));
            }
        }

        public static async Task<(Core core, Dictionary<string, byte[]> verifyfilepaths,
                        MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore)> InitializeNewCoreWithStandardFiles(
            int nonencrypteddsts, int encrypteddsts, Random? random = null, bool cache = true, int regFileCount = 100)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS(nonencrypteddsts + encrypteddsts);
            BPlusTree<byte[]> vfsdatastore = new(10);
            MetadataNode? vfsDirectory = vfsroot.GetDirectory("src");
            if (vfsDirectory == null)
            {
                throw new NullReferenceException();
            }

            Dictionary<string, byte[]> verifyfilepaths = AddStandardVFSFiles(vfsDirectory, vfsdatastore, random, regFileCount);
            var vfsisrc = new VirtualFSInterop(vfsroot, vfsdatastore);

            List<ICoreDstDependencies> destinations = new();
            for (int i = 0; i < nonencrypteddsts + encrypteddsts; i++)
            {
                IDstFSInterop vfsidst;
                if (i < nonencrypteddsts)
                {
                    vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, Path.Combine("dst", i.ToString()));
                }
                else
                {
                    vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, Path.Combine("dst", i.ToString()), "password");
                }
                ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", false, vfsidst, cache);
                destinations.Add(dstdeps);
            }
            
            var vfsicache = await VirtualFSInterop.InitializeNewDst(vfsroot, vfsdatastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc, "cache");
            ICoreDstDependencies? cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.InitializeNew("test", true, vfsicache, false);
            Core core = new(srcdeps, destinations, cachedeps);
            return (core, verifyfilepaths, vfsroot, vfsdatastore);
        }

        public static (byte[] hash, byte[] file) MakeRandomFile(int size, Random? random=null)
        {
            byte[] data = new byte[size];
            if (random==null)
            {
                random = new Random();
            }
            random.NextBytes(data);
            return (Hasher.ComputeHash(data), data);
        }

        public static async Task InitializeNew(bool cache, int nonencrypteddsts, int encrypteddsts)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS(nonencrypteddsts + encrypteddsts);
            BPlusTree<byte[]> datastore = new(10);
            var vfsisrc = new VirtualFSInterop(vfsroot, datastore);

            List<ICoreDstDependencies> destinations = new();
            for (int i = 0; i < nonencrypteddsts + encrypteddsts; i++)
            {
                IDstFSInterop vfsidst;
                if (i < nonencrypteddsts)
                {
                    vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, Path.Combine("dst", i.ToString()));
                }
                else
                {
                    vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, Path.Combine("dst", i.ToString()), "password");
                }
                destinations.Add(CoreDstDependencies.InitializeNew("test", false, vfsidst, true));
            }

            var vfsicache = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc);
            if (cache)
            {
                ICoreDstDependencies cachedeps = CoreDstDependencies.InitializeNew("test", true, vfsicache, false);
                _ = new Core(srcdeps, destinations, cachedeps);
            }
            else
            {
                _ = new Core(srcdeps, destinations);
            }
        }

        [TestMethod]
        public void TestInitializeNew()
        {
            InitializeNew(true, 1, 0).Wait();
            InitializeNew(false, 0, 1).Wait();
        }

        public static async Task LoadCore_NewlyInitialized(bool encrypted, bool cache)
        {
            MetadataNode vfsroot = CreateBasicVirtualFS(1);
            BPlusTree<byte[]> datastore = new(10);
            var vfsisrc = new VirtualFSInterop(vfsroot, datastore);
            IDstFSInterop vfsidst;
            if (encrypted)
            {
                vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, Path.Combine("dst", "1"), "password");
            } else
            {
                vfsidst = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, Path.Combine("dst", "1"));
            }
            var vfsicache = await VirtualFSInterop.InitializeNewDst(vfsroot, datastore, "cache");
            ICoreSrcDependencies srcdeps = FSCoreSrcDependencies.InitializeNew("test", "src", vfsisrc, "cache");
            ICoreDstDependencies dstdeps = CoreDstDependencies.InitializeNew("test", false, vfsidst, true);
            ICoreDstDependencies? cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.InitializeNew("test", true, vfsicache, false);
            Core core = new(srcdeps, new List<ICoreDstDependencies>() { dstdeps }, cachedeps);
            Assert.IsTrue(core.DefaultDstDependencies.Count == 1);

            vfsisrc = new VirtualFSInterop(vfsroot, datastore);
            if (encrypted)
            {
                vfsidst = await VirtualFSInterop.LoadDst(vfsroot, datastore, Path.Combine("dst", "1"), "password");
            }
            else
            {
                vfsidst = await VirtualFSInterop.LoadDst(vfsroot, datastore, Path.Combine("dst", "1"));
            }
            vfsicache = await VirtualFSInterop.LoadDst(vfsroot, datastore, "cache");
            srcdeps = FSCoreSrcDependencies.Load("src", vfsisrc);
            dstdeps = CoreDstDependencies.Load(vfsidst, true);
            cachedeps = null;
            if (cache) cachedeps = CoreDstDependencies.Load(vfsicache, false);
            _ = new Core(srcdeps, new List<ICoreDstDependencies>() { dstdeps }, cachedeps);
        }

        [TestMethod]
        public void TestLoadCore_NewlyInitialized()
        {
            LoadCore_NewlyInitialized(false, false).Wait();
            LoadCore_NewlyInitialized(true, true).Wait();
        }

        public static void RunBackup(bool cache, int nonencrypteddsts, int encrypteddsts)
        {
            var (core, _, vfsroot, _) = 
                InitializeNewCoreWithStandardFiles(nonencrypteddsts, encrypteddsts, cache: cache).Result;

            core.RunBackup("test", "run1");
            vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            System.Threading.Thread.Sleep(40); // Allow async writes to finish
            core.RunBackup("test", "run2");
        }

        [TestMethod]
        public void TestRunBackup()
        {
            RunBackup(true, 1, 0);
            RunBackup(false, 0, 1);
        }

        [TestMethod]
        public void TestRunMultiBackup()
        {
            RunBackup(true, 2, 2);
        }

        public static void GetWTStatus(bool encrypted, bool cache)
        {
            (Core core, Dictionary<string, byte[]> verifyfilepaths,
                MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) testdata;
            if (encrypted) {
                testdata = InitializeNewCoreWithStandardFiles(0, 1, cache: cache).Result;
            }
            else
            {
                testdata = InitializeNewCoreWithStandardFiles(1, 0, cache: cache).Result;
            }

            testdata.core.GetWTStatus("test");
            // TODO: test output
        }

        [TestMethod]
        public void TestGetWTStatus()
        {
            GetWTStatus(false, false);
            GetWTStatus(true, true);
        }

        public static void Restore(bool encrypted, bool cache)
        {
            (Core core, Dictionary<string, byte[]> verifyfilepaths,
                MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) testdata;
            if (encrypted)
            {
                testdata = InitializeNewCoreWithStandardFiles(0, 1, cache: cache).Result;
            }
            else
            {
                testdata = InitializeNewCoreWithStandardFiles(1, 0, cache: cache).Result;
            }
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

        public static void RemoveBackup(bool encrypted, bool cache, Random? random = null)
        {
            (Core core, Dictionary<string, byte[]> verifyfilepaths,
                MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) testdata;
            if (encrypted)
            {
                testdata = InitializeNewCoreWithStandardFiles(0, 1, cache: cache, random: random).Result;
            }
            else
            {
                testdata = InitializeNewCoreWithStandardFiles(1, 0, cache: cache, random: random).Result;
            }
            var (core, _, vfsroot, _) = testdata;

            var bh1 = core.RunBackup("test", "run1");
            //System.Threading.Thread.Sleep(40); // Allow async writes to finish

            vfsroot.AddDirectory("src", VirtualFSInterop.MakeNewDirectoryMetadata("sub"));
            var bh2 = core.RunBackup("test", "run2");
            //System.Threading.Thread.Sleep(40); // Allow async writes to finish

            // Full hash test
            core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh1.GetOrThrow()));
            //System.Threading.Thread.Sleep(40); // Allow async writes to finish
            // Just prefix
            core.RemoveBackup("test", HashTools.ByteArrayToHexViaLookup32(bh2.GetOrThrow())[..10]);
            // All backups deleted
            Assert.AreEqual(core.GetBackups("test").backups.Count(), 0);
        }

        [TestMethod]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0059:Unnecessary assignment of a value", Justification = "<Pending>")]
        public void TestRemoveBackup()
        {
            //RemoveBackup(false, false);
            int encryptedFails = 0;
            int nonEncryptedFails = 0;

            bool encrypt = false;
            for (int i = 0; i < 10; i++)
            {
                encrypt ^= true;
                try
                {
                    RemoveBackup(encrypt, true, new Random(91));
                }
                catch (Exception e)
                {
                    if (encrypt)
                    {
                        encryptedFails++;
                    }
                    else
                    {
                        nonEncryptedFails++;
                    }
                }
            }
            Console.WriteLine(String.Format("{0} encrypted fails", encryptedFails));
            Console.WriteLine(String.Format("{0} non encrypted fails", nonEncryptedFails));
        }

        public static async Task TransferBackupSet(bool encrypted, bool cache, Random? random=null, int regFileCount=100)
        {
            (Core core, Dictionary<string, byte[]> verifyfilepaths,
                MetadataNode vfsroot, BPlusTree<byte[]> vfsdatastore) testdata;
            if (encrypted)
            {
                testdata = await InitializeNewCoreWithStandardFiles(0, 1, random, cache, regFileCount);
            }
            else
            {
                testdata = await InitializeNewCoreWithStandardFiles(1, 0, random, cache, regFileCount);
            }
            var (srcCore, _, _, _) = testdata;
            
            if (encrypted)
            {
                testdata = await InitializeNewCoreWithStandardFiles(0, 1, random, cache, regFileCount);
            }
            else
            {
                testdata = await InitializeNewCoreWithStandardFiles(1, 0, random, cache, regFileCount);
            }
            var (dstCore, _, _, _) = testdata;

            _ = srcCore.RunBackup("test", "run1");
            var e = srcCore.DefaultDstDependencies[0].DstFSInterop.Encryptor;

            srcCore.TransferBackupSet(new BackupSetReference("test", false, false, false), dstCore, true);
        }

        [TestMethod]
        public void TestTransferBackupSet()
        {
            for (int i = 0; i < 5; i++)
            {
                TransferBackupSet(false, true, new Random(1000), 2).Wait();
                TransferBackupSet(true, false, new Random(1000), 2).Wait();
            }
        }

        [TestMethod]
        public void TestPathMatchesPattern()
        {
            string path1 = "a/b/c/d/efg/h.i";
            string pattern1 = "*c*g*";
            Assert.IsTrue(BackupCalculation.PatternMatchesPath(path1, pattern1));
        }

        [TestMethod]
        public void TestCheckTrackFile()
        {
            List<(int, string)> patterns = new()
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
                Assert.AreEqual(BackupCalculation.FileTrackClass(files[i], patterns), correctoutput[i]);
            }
        }
        
        [TestMethod]
        public void TestCheckTrackAnyDirectoryChild()
        {
            List<(int, string)> patterns = new()
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
                var res = BackupCalculation.CheckTrackAnyDirectoryChild(directories[i], patterns);
                Assert.AreEqual(res, correctoutput[i]);
            }
        }

        public static void RandomData(byte[] data)
        {
            RandomData(data, new Random());
        }

        public static void RandomData(byte[] data, Random rng)
        {
            rng.NextBytes(data);
        }
    }
#pragma warning restore CS0168 // Variable is declared but never used
}