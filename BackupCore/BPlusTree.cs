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
            // Traverse down the tree
            BPlusTreeNode node = FindLeafNode(hash);

            bool dosave = AddKeyToNode(node, hash, blocation);

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

        private bool AddKeyToNode(BPlusTreeNode node, byte[] hash, BackupLocation blocation)
        {
            if (node.IsLeafNode != true)
            {
                throw new ArgumentException("A child node must be specified with the key"
                    + " if the node to be added to is an interior node.");
            }
            // Look for key in node
            int position = 0;
            for (; position < node.Keys.Count && !HashTools.ByteArrayLessThanEqualTo(hash, node.Keys[position]); position++) { }
            // Hash already exists in BPlusTree, return true
            if (position < node.Keys.Count && node.Keys[position].SequenceEqual(hash))
            {
                return true;
            }
            // Hash not in tree, belongs in position
            else
            {


                // Go ahead and add the new key/value then split as normal
                node.Keys.Insert(position, hash);
                node.Values.Insert(position, blocation);

                // Is this node full?
                if (node.Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
                {
                    // Create a new node and add half of this node's keys/ values to it
                    BPlusTreeNode newnode = new BPlusTreeNode(node.Parent, true, NodeSize, node.Next);
                    node.Next = newnode;
                    newnode.Keys = node.Keys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    newnode.Values = node.Values.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    node.Keys.RemoveRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    node.Values.RemoveRange(node.Values.Count / 2, node.Values.Count - (node.Values.Count / 2));
                    // Add the new node to its parent
                    AddKeyToNode(node.Parent, newnode.Keys[0], newnode);
                }
                return false;
            }
        }

        public void AddKeyToNode(BPlusTreeNode node, byte[] hash, BPlusTreeNode child)
        {
            if (node.IsLeafNode == true)
            {
                throw new ArgumentException("A value must be specified with the key"
                    + " if the node to be added to is a leaf node.");
            }
            // Look for where to put key in node
            int position = 0;
            for (; position < node.Keys.Count && !HashTools.ByteArrayLessThan(hash, node.Keys[position]); position++) { }
            // Key "can't" already be in node because it was split from a lower node
            node.Keys.Insert(position, hash);
            node.Children.Insert(position + 1, child);
            // Is this node full?
            if (node.Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
            {
                // Create a new node and add half of this node's keys/ children to it
                BPlusTreeNode newnode = new BPlusTreeNode(node.Parent, false, NodeSize);
                newnode.Keys = node.Keys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                newnode.Children = node.Children.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2) + 1);
                int keycount = node.Keys.Count;
                node.Keys.RemoveRange(keycount / 2, keycount - (keycount / 2));
                node.Children.RemoveRange(keycount / 2, keycount - (keycount / 2) + 1);
                byte[] split = node.Keys[node.Keys.Count - 1];
                node.Keys.RemoveAt(node.Keys.Count - 1);

                // Make all newly split out Children refer to newnode as their parent
                foreach (var nchild in newnode.Children)
                {
                    nchild.Parent = newnode;
                }

                // Are we !at the root
                if (node.Parent != null)
                {
                    // Add the new node to its parent
                    AddKeyToNode(node.Parent, split, newnode);
                }
                else
                {
                    // We just split the root, so make a new one
                    BPlusTreeNode root = new BPlusTreeNode(null, false, NodeSize);
                    node.Parent = root;
                    node.Parent.Keys = new List<byte[]>();
                    node.Parent.Keys.Add(split);
                    node.Parent.Children = new List<BPlusTreeNode>();
                    node.Parent.Children.Add(node);
                    node.Parent.Children.Add(newnode);
                    newnode.Parent = node.Parent;
                }
            }
        }

        private BackupLocation GetRecordFromNode(BPlusTreeNode node, byte[] hash)
        {
            if (node.IsLeafNode != true)
            {
                throw new ArgumentException("Get Record only works on interior nodes.");
            }
            for (int i = 0; i < node.Keys.Count; i++)
            {
                if (node.Keys[i].SequenceEqual(hash))
                {
                    return node.Values[i];
                }
            }
            return null;
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
            return GetRecordFromNode(FindLeafNode(hash), hash);
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
