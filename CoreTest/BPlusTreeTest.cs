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
        public void TestAddRemoveFew()
        {
            var BPTree = new BPlusTree<BlobLocation>(100);

            var testblob1 = new BlobLocation("somewhere1", 0, 40);
            BlobLocation bl2 = new BlobLocation("somewhere2", 4, 401);
            BlobLocation bl3 = new BlobLocation("somewhere3", 0, 440);
            BlobLocation bl4 = new BlobLocation("somewhere4", 300, 74000);

            var rng = new Random();

            var testkey1 = new byte[20];
            rng.NextBytes(testkey1);
            byte[] key2 = new byte[20];
            rng.NextBytes(key2);
            byte[] key3 = new byte[20];
            rng.NextBytes(key3);
            byte[] key4 = new byte[20];
            rng.NextBytes(key4);
            byte[] key5 = new byte[20];
            rng.NextBytes(key5);

            BPTree.AddOrFind(testkey1, testblob1);
            BPTree.AddOrFind(key2, bl2);
            BPTree.AddOrFind(key3, bl3);
            BPTree.AddOrFind(key4, bl4);

            BPTree.AddOrFind(key5, bl4);
            BPTree.AddOrFind(testkey1, testblob1);

            List<KeyValuePair<byte[], BlobLocation>> treecontents1 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            BPTree.Remove(testkey1);
            List<KeyValuePair<byte[], BlobLocation>> treecontents2 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            BPTree.AddOrFind(testkey1, testblob1);
            List<KeyValuePair<byte[], BlobLocation>> treecontents3 = new List<KeyValuePair<byte[], BlobLocation>>(BPTree);
            Assert.IsFalse(TreeContentsMatch(treecontents1, treecontents2));
            Assert.IsTrue(TreeContentsMatch(treecontents1, treecontents3));
        }

        [TestMethod]
        public void TestAddRemoveMany()
        {
            var BPTree = new BPlusTree<byte[]>(100);
            Random random = new Random(80);
            List<byte[]> keyvals = new List<byte[]>();
            for (int i = 0; i < 107; i++)
            {
                byte[] keyval = new byte[20];
                random.NextBytes(keyval);
                BPTree.Add(keyval, keyval);
                keyvals.Add(keyval);
            }
            ValidateTree(BPTree, keyvals);

            for (int i = 0; i < 107; i++)
            {
                int remidx = random.Next(keyvals.Count);
                BPTree.Remove(keyvals[remidx]);
                keyvals.RemoveAt(remidx);
                ValidateTree(BPTree, keyvals);
            }
        }

        private void ValidateTree(BPlusTree<byte[]> tree, List<byte[]> keyvals)
        {
            foreach (var keyval in keyvals)
            {
                var val = tree.GetRecord(keyval);
                Assert.IsTrue(val.SequenceEqual(keyval));
            }
        }

        [TestMethod]
        public void TestAddMany()
        {
            var bptree = new BPlusTree<string>(100);
            for (int i = 0; i < 200; i++)
            {
                bptree.Add(BitConverter.GetBytes(i).Reverse().ToArray(), i.ToString());
            }
            Assert.IsTrue(bptree.Count == 200);
            Assert.IsTrue(KVPSequenceEqual(Enumerable.Range(0, 200)
                .Select(i => new KeyValuePair<byte[], string>(BitConverter.GetBytes(i)
                .Reverse().ToArray(), i.ToString())), bptree));
            for (int i = 200; i < 200000; i++)
            {
                bptree.Add(BitConverter.GetBytes(i).Reverse().ToArray(), i.ToString());
            }
            Assert.IsTrue(bptree.Count == 200000);
            Assert.IsTrue(KVPSequenceEqual(Enumerable.Range(0, 200000)
                .Select(i => new KeyValuePair<byte[], string>(BitConverter.GetBytes(i)
                .Reverse().ToArray(), i.ToString())), bptree));
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

        private bool KVPSequenceEqual<V>(IEnumerable<KeyValuePair<byte[], V>> kvp1, IEnumerable<KeyValuePair<byte[], V>> kvp2)
        {
            var enum1 = kvp1.GetEnumerator();
            var enum2 = kvp2.GetEnumerator();
            int i = 0;
            while (enum1.MoveNext())
            {
                enum2.MoveNext();
                if (!enum1.Current.Value.Equals(enum2.Current.Value))
                {
                    return false;
                }
                if (!enum1.Current.Key.SequenceEqual(enum2.Current.Key))
                {
                    return false;
                }
                i++;
            }
            return true;
        }
    }
}
