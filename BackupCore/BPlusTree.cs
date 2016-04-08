using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BPlusTree : IEnumerable<KeyValuePair<byte[], BackupLocation>>
    {
        public BPlusTreeNode Root { get; set; }

        // Head of linked list allowing for efficient in order traversal of leaf nodes
        private BPlusTreeNode Head { get; set; }

        public int NodeSize { get; set; }

        public BPlusTree(int nodesize)
        {
            NodeSize = nodesize;
            Root = new BPlusTreeNode(null, false, NodeSize);
            BPlusTreeNode rootchild2 = new BPlusTreeNode(Root, true, NodeSize);
            BPlusTreeNode rootchild1 = new BPlusTreeNode(Root, true, NodeSize, rootchild2);
            Root.Children.Add(rootchild1);
            Root.Children.Add(rootchild2);
            Root.Keys.Add(HashTools.HexStringToByteArray("8000000000000000000000000000000000000000"));
            Head = rootchild1;
        }

        /// <summary>
        /// Adds a hash and backuplocation to the tree
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blocation"></param>
        /// <returns>True if hash already exists in tree, False otherwise.</returns>
        public bool AddHash(byte[] hash, BackupLocation blocation)
        {
            if (hash[0] == 60)
            {
                
            }
            // Traverse down the tree
            BPlusTreeNode node = FindLeafNode(hash);
            bool dosave = node.AddKey(hash, blocation);
            // Was the root node split?
            if (Root.Parent != null)
            {
                Root = Root.Parent;
            }
            if (Root.Children.Count == 9)
            {

            }
            if (!dosave)
            {
                //PrintTree();
            }
            return dosave;
        }

        private BPlusTreeNode FindLeafNode(byte[] hash)
        {
            // Traverse down the tree
            BPlusTreeNode node = Root;
            while (!node.IsLeafNode)
            {
                int child = 0;
                for (; child < node.Keys.Count && !HashTools.ByteArrayLessThan(hash, node.Keys[child]); child++) { }
                node = node.Children[child];
            }
            return node;
        }

        private void PrintTree()
        {
            using (System.IO.StreamWriter file =
            new System.IO.StreamWriter(@"C:\Users\Wesley\Desktop\tree.txt", true))
            {
                Queue<BPlusTreeNode> printqueue = new Queue<BPlusTreeNode>();
                printqueue.Enqueue(Root);
                file.WriteLine('*');
                while (printqueue.Count > 0)
                {
                    var node = printqueue.Dequeue();
                    foreach (var key in node.Keys)
                    {
                        file.WriteLine(key[0]);
                    }
                    file.WriteLine('-');
                    if (!node.IsLeafNode)
                    {
                        foreach (var child in node.Children)
                        {
                            printqueue.Enqueue(child);
                        }
                    }
                }
            }

        }

        public BackupLocation GetRecord(byte[] hash)
        {
            return FindLeafNode(hash).GetRecord(hash);
        }

        public IEnumerator<KeyValuePair<byte[], BackupLocation>> GetEnumerator()
        {
            BPlusTreeNode node = Head;
            while (node != null)
            {
                for (int i = 0; i < node.Keys.Count; i++)
                {
                    yield return new KeyValuePair<byte[], BackupLocation>(node.Keys[i], node.Values[i]);
                }
                node = node.Next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
