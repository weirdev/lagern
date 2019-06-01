using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace CoreTest
{
    /// <summary>
    /// Summary description for BlobStoreTest
    /// </summary>
    [TestClass]
    public class BlobStoreTest
    {
        public BlobStore BS { get; set; }

        private MetadataNode VirtualFS { get; set; }

        private BPlusTree<byte[]> VFSDataStore { get; set; }
        
        [TestInitialize]
        public void Initialize()
        {
            VirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            VFSDataStore = new BPlusTree<byte[]>(10);
            BS = new BlobStore(new BlobStoreDependencies(VirtualFSInterop.InitializeNewDst(VirtualFS, VFSDataStore, "dst")));
        }

        private TestContext testContextInstance;

        /// <summary>
        ///Gets or sets the test context which provides
        ///information about and functionality for the current test run.
        ///</summary>
        public TestContext TestContext
        {
            get
            {
                return testContextInstance;
            }
            set
            {
                testContextInstance = value;
            }
        }

        #region Additional test attributes
        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        //
        // Use TestCleanup to run code after each test has run
        // [TestCleanup()]
        // public void MyTestCleanup() { }
        //
        #endregion

        [TestMethod]
        public void TestSplitData()
        {
            // We test by first making 8 total random blocks of data a-h

            // The Data are arranged as follows
            //  [ small block_a ] = 2 KB
            //  [ large block_b ] = 2 MB
            //  [ small block_c ] = 8 bytes
            //  [ large block_d ] = 4 MB
            //  [ small block_e ] = 4 KB

            // These are then concenated then split by BS.SplitData()

            // Small blocks a and e are exchanged for equally sized random blocks f, h
            // Small block c is replaced with g, a block of size 1
            // Concenated and split again
            // Make sure when hashes match data is same length
            // Verify size of data represented by "new" hashes is < 20 MB

            byte[] small_a = new byte[2048];
            CoreTest.RandomData(small_a);
            byte[] large_b = new byte[2_971_520];
            CoreTest.RandomData(large_b);
            byte[] small_c = new byte[8];
            CoreTest.RandomData(small_c);
            byte[] large_d = new byte[4_943_040];
            CoreTest.RandomData(large_d);
            byte[] small_e = new byte[4096];
            CoreTest.RandomData(small_e);
            byte[] small_f = new byte[2048];
            CoreTest.RandomData(small_f);
            byte[] small_g = new byte[1];
            CoreTest.RandomData(small_g);
            byte[] small_h = new byte[4096];
            CoreTest.RandomData(small_h);

            byte[] file1 = ConcenateFile(new byte[][] { small_a, large_b, small_c, large_d, small_e });
            byte[] file2 = ConcenateFile(new byte[][] { small_f, large_b, small_g, large_d, small_h });

            BlockingCollection<HashBlobPair> fileblockqueue = new BlockingCollection<HashBlobPair>();
            byte[] file1hash = new byte[20]; // Overall hash of file
            BlobStore.SplitData(new MemoryStream(file1), file1hash, fileblockqueue);

            List<HashBlobPair> f1blockshashes = new List<HashBlobPair>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlobPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    f1blockshashes.Add(block);
                }
            }

            fileblockqueue = new BlockingCollection<HashBlobPair>();
            byte[] file2hash = new byte[20]; // Overall hash of file
            BlobStore.SplitData(new MemoryStream(file2), file2hash, fileblockqueue);

            List<HashBlobPair> f2blockshashes = new List<HashBlobPair>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlobPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    f2blockshashes.Add(block);
                }
            }

            Assert.IsFalse(file1hash.SequenceEqual(file2hash));

            int sizeaddition = VFSDataStore.Select((kvp, _) => kvp.Value.Length).Sum();
            Assert.IsTrue(sizeaddition < file1.Length + file2.Length); // TODO: Magic number?
        }

        // TODO: test serialize and deserialize with explicitly added blobs of
        // all blob types as well as after backup
        [TestMethod]
        public void TestBlobStoreSerialize()
        {
            var testdata = CoreTest.InitializeNewCoreWithStandardFiles();
            testdata.core.RunBackup("test", "initialrun");
            byte[] serialized = testdata.core.DefaultDstDependencies[0].Blobs.serialize();

            // Test that something was serialized
            Assert.IsTrue(serialized.Length > 0);
            // Test that some bytes are nonzero
            Assert.IsFalse(serialized.SequenceEqual(new byte[serialized.Length]));
        }

        [TestMethod]
        public void TestBlobStoreDeserialize()
        {
            var testdata = CoreTest.InitializeNewCoreWithStandardFiles();
            testdata.core.RunBackup("test", "initialrun");
            byte[] serialized = testdata.core.DefaultDstDependencies[0].Blobs.serialize();
            var bs = BlobStore.deserialize(serialized, testdata.core.DefaultDstDependencies[0].Blobs.Dependencies);
        }

        [TestMethod]
        public void TestTransferBlobAndReferences()
        {
            var dstVirtualFS = new MetadataNode(VirtualFSInterop.MakeNewDirectoryMetadata("c"), null);
            var dstVFSDataStore = new BPlusTree<byte[]>(10);
            var dstBS = new BlobStore(new BlobStoreDependencies(VirtualFSInterop.InitializeNewDst(dstVirtualFS, dstVFSDataStore, "dst")));

            // FileBlob
            byte[] randomFile = new byte[100];
            CoreTest.RandomData(randomFile);
            byte[] fileHash = BlobStore.StoreData(new List<BlobStore>(1) { BS }, "test", randomFile);
            BS.TransferBlobAndReferences(dstBS, "test", fileHash, BlobLocation.BlobType.FileBlob, true);
            Assert.IsTrue(dstBS.RetrieveData(fileHash).SequenceEqual(randomFile));

            // Likely multiblock
            randomFile = new byte[12_000];
            CoreTest.RandomData(randomFile);
            fileHash = BlobStore.StoreData(new List<BlobStore>(1) { BS }, "test", randomFile);
            BS.TransferBlobAndReferences(dstBS, "test", fileHash, BlobLocation.BlobType.FileBlob, true);
            Assert.IsTrue(dstBS.RetrieveData(fileHash).SequenceEqual(randomFile));

            // TODO: Deep and shallow for:
            // MetadataNode
            // BackupRecord
        }

        public byte[] ConcenateFile(byte[][] fileblocks)
        {
            int size = 0;
            foreach (var block in fileblocks)
            {
                size += block.Length;
            }
            byte[] file = new byte[size];
            int index = 0;
            foreach (var block in fileblocks)
            {
                Array.Copy(block, 0, file, index, block.Length);
                index += block.Length;
            }
            return file;
        }
    }
}
