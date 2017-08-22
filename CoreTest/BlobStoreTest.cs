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
        
        public BlobStoreTest()
        {
            BS = new BlobStore(null, null, false); // keep this entirely in memory
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
            //  [ large block_b ] = 20 MB
            //  [ small block_c ] = 8 bytes
            //  [ large block_d ] = 40 MB
            //  [ small block_e ] = 4 KB

            // These are then concenated then split by BS.SplitData()

            // Small blocks a and e are exchanged for equally sized random blocks f, h
            // Small block c is replaced with g, a block of size 1
            // Concenated and split again
            // Make sure when hashes match data is same length
            // Verify size of data represented by "new" hashes is < 20 MB

            byte[] small_a = new byte[2048];
            RandomData(small_a);
            byte[] large_b = new byte[20971520];
            RandomData(large_b);
            byte[] small_c = new byte[8];
            RandomData(small_c);
            byte[] large_d = new byte[41943040];
            RandomData(large_d);
            byte[] small_e = new byte[4096];
            RandomData(small_e);
            byte[] small_f = new byte[2048];
            RandomData(small_f);
            byte[] small_g = new byte[1];
            RandomData(small_g);
            byte[] small_h = new byte[4096];
            RandomData(small_h);

            byte[] file1 = ConcenateFile(new byte[][] { small_a, large_b, small_c, large_d, small_e });
            byte[] file2 = ConcenateFile(new byte[][] { small_f, large_b, small_g, large_d, small_h });

            BlockingCollection<HashBlobPair> fileblockqueue = new BlockingCollection<HashBlobPair>();
            byte[] file1hash = new byte[20]; // Overall hash of file
            BS.SplitData(new MemoryStream(file1), file1hash, fileblockqueue);

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
            BS.SplitData(new MemoryStream(file2), file2hash, fileblockqueue);

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

            int i1 = 0;
            int sizeaddition = 0;
            for (int i2 = 0; i2 < f2blockshashes.Count; i2++)
            {
                if (i1 >= f1blockshashes.Count)
                {
                    sizeaddition += f2blockshashes[i2].Block.Length;
                }
                else
                {
                    int j;
                    for (j = i1; j < f1blockshashes.Count; j++)
                    {
                        if (f1blockshashes[j].Hash.SequenceEqual(f2blockshashes[i2].Hash))
                        {
                            Assert.IsTrue(f1blockshashes[j].Block.Length == f2blockshashes[i2].Block.Length);
                        }
                    }
                    if (j == f1blockshashes.Count)
                    {
                        sizeaddition += f2blockshashes[i2].Block.Length;
                    }
                }
            }

            Console.WriteLine(sizeaddition);
            Assert.IsTrue(sizeaddition < 209715200);
        }

        public void RandomData(byte[] data)
        {
            Random rng = new Random();
            rng.NextBytes(data);
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
