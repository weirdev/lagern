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
        }

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
                for (; !HashTools.ByteArrayLessThan(node.Keys[child], hash); child++) { }
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
