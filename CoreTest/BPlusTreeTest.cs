using System;
using System.Text;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using BackupCore;
using System.Linq;

namespace CoreTest
{
    /// <summary>
    /// Summary description for UnitTest1
    /// </summary>
    [TestClass]
    public class BPlusTreeTest
    {
        public BPlusTreeTest()
        {
            BPTree = new BPlusTree<BlobLocation>(100);

            testblob1 = new BlobLocation(BlobLocation.BlobTypes.FileBlob, false, "somewhere1", 0, 40);
            BlobLocation bl2 = new BlobLocation(BlobLocation.BlobTypes.FileBlob, false, "somewhere2", 4, 401);
            BlobLocation bl3 = new BlobLocation(BlobLocation.BlobTypes.FileBlob, false, "somewhere3", 0, 440);
            BlobLocation bl4 = new BlobLocation(BlobLocation.BlobTypes.FileBlob, false, "somewhere4", 300, 74000);

            var rng = new Random();

            testkey1 = new byte[20];
            rng.NextBytes(testkey1);
            byte[] key2 = new byte[20];
            rng.NextBytes(key2);
            byte[] key3 = new byte[20];
            rng.NextBytes(key3);
            byte[] key4 = new byte[20];
            rng.NextBytes(key4);
            byte[] key5 = new byte[20];
            rng.NextBytes(key5);

            BPTree.AddHash(testkey1, testblob1);
            BPTree.AddHash(key2, bl2);
            BPTree.AddHash(key3, bl3);
            BPTree.AddHash(key4, bl4);

            BPTree.AddHash(key5, bl4);
            BPTree.AddHash(testkey1, testblob1);
        }

        public BPlusTree<BlobLocation> BPTree { get; set; }
        public BlobLocation testblob1 { get; set; }
        public byte[] testkey1 { get; set; }

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

        // Serialize no longer in B+ tree generic class
        // TODO: Add serialization tests for classes using/inheriting B+ tree

        [TestMethod]
        public void TestAddRemove()
        {
            List<KeyValuePair<byte[], BlobLocation>> treecontents1 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            BPTree.RemoveKey(testkey1);
            List<KeyValuePair<byte[], BlobLocation>> treecontents2 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            BPTree.AddHash(testkey1, testblob1);
            List<KeyValuePair<byte[], BlobLocation>> treecontents3 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            Assert.IsFalse(TreeContentsMatch(treecontents1, treecontents2));
            Assert.IsTrue(TreeContentsMatch(treecontents1, treecontents3));
        }

        private bool TreeContentsMatch(List<KeyValuePair<byte[], BlobLocation>> tc1, List<KeyValuePair<byte[], BlobLocation>> tc2)
        {
            if (tc1.Count != tc2.Count)
            {
                return false;
            }
            for (int i = 0; i < tc1.Count; i++)
            {
                if (tc1[i].Value != tc2[i].Value)
                {
                    return false;
                }
                if (!tc1[i].Key.SequenceEqual(tc2[i].Key))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
