using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BPlusTree
    {
        public BPlusTreeNode Root { get; set; }

        public int NodeSize { get; set; }

        public BPlusTree(int nodesize)
        {
            NodeSize = nodesize;
            Root = new BPlusTreeNode(null, false, NodeSize);
            BPlusTreeNode rootchild1 = new BPlusTreeNode(Root, true, NodeSize);
            BPlusTreeNode rootchild2 = new BPlusTreeNode(Root, true, NodeSize);
            Root.Children.Add(rootchild1);
            Root.Children.Add(rootchild2);
            Root.Keys.Add(HashTools.HexStringToByteArray("8000000000000000000000000000000000000000"));
        }

        /// <summary>
        /// Adds a hash and backuplocation to the tree
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blocation"></param>
        /// <returns>True if hash already exists in tree, False otherwise.</returns>
        public bool AddHash(byte[] hash, BackupLocation blocation)
        {
            // Traverse down the tree
            BPlusTreeNode node = FindLeafNode(hash);
            bool dosave = node.AddKey(hash, blocation);
            // Was the root node split?
            if (Root.Parent != null)
            {
                Root = Root.Parent;
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
                for (; child < node.Keys.Count && !HashTools.ByteArrayLessThan(node.Keys[child], hash); child++) { }
                node = node.Children[child];
            }
            return node;
        }

        public BackupLocation GetRecord(byte[] hash)
        {
            return FindLeafNode(hash).GetRecord(hash);
        }
    }
}
