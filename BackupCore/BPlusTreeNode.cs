using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BPlusTreeNode
    {
        public BPlusTreeNode Parent { get; set; }
        public int NodeSize { get; set; }

        public List<byte[]> Keys { get; set; }

        public bool IsLeafNode { get; set; }
        
        // Size 100
        public List<BPlusTreeNode> Children { get; set; }
        
        // Size 99
        public List<BackupLocation> Values { get; set; }

        public BPlusTreeNode(BPlusTreeNode parent, bool isleafnode, int nodesize)
        {
            Parent = parent;
            NodeSize = nodesize;
            IsLeafNode = isleafnode;
            Keys = new List<byte[]>();
            if (IsLeafNode)
            {
                Values = new List<BackupLocation>();
            }
            else
            {
                Children = new List<BPlusTreeNode>();
            }
        }

        /// <summary>
        /// Adds a key/value to a leaf node
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="value"></param>
        /// <returns>True if the key already exists in this node. False otherwise.</returns>
        public bool AddKey(byte[] hash, BackupLocation value)
        {
            if (IsLeafNode != true)
            {
                throw new ArgumentException("A child node must be specified with the key" 
                    + " if the node to be added to is an interior node.");
            }
            // Look for key in node
            int position = 0;
            for (; position < Keys.Count && !HashTools.ByteArrayLessThan(Keys[position], hash); position++) { }
            // Hash already exists in BPlusTree, return true
            if (position < Keys.Count && Keys[position].SequenceEqual(hash))
            {
                return true;
            }
            // Hash not in tree, belongs in position "value"
            else
            {
                // Go ahead and add the new key/value then split as normal
                Keys.Insert(position, hash);
                Values.Insert(position, value);

                // Is this node full?
                if (Keys.Count > (NodeSize-1)) // Nodesize-1 for keys
                {
                    // Create a new node and add half of this node's keys/ values to it
                    BPlusTreeNode newnode = new BPlusTreeNode(Parent, true, NodeSize);
                    newnode.Keys = Keys.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                    newnode.Values = Values.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                    Keys.RemoveRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                    Values.RemoveRange(Values.Count / 2, Values.Count - (Values.Count / 2));
                    // Add the new node to its parent
                    Parent.AddKey(newnode.Keys[0], newnode);
                }
                return false;
            }
        }

        /// <summary>
        /// Adds a key to this internal node connecting this node with child.
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="child"></param>
        public void AddKey(byte[] hash, BPlusTreeNode child)
        {
            if (IsLeafNode == true)
            {
                throw new ArgumentException("A value must be specified with the key"
                    + " if the node to be added to is a leaf node.");
            }
            // Look for where to put key in node
            int position = 0;
            for (; position < Keys.Count && !HashTools.ByteArrayLessThan(Keys[position], hash); position++) { }
            // Key "can't" already be in node because it was split from a lower node
            Keys.Insert(position, hash);
            Children.Insert(position + 1, child);
            // Is this node full?
            if (Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
            {
                // Create a new node and add half of this node's keys/ children to it
                BPlusTreeNode newnode = new BPlusTreeNode(Parent, true, NodeSize);
                newnode.Keys = Keys.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                newnode.Children = Children.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                int keycount = Keys.Count;
                Keys.RemoveRange(keycount / 2, keycount - (keycount / 2));
                Children.RemoveRange(keycount / 2, keycount - (keycount / 2));
                byte[] split = Keys[Keys.Count - 1];
                Keys.RemoveAt(Keys.Count - 1);

                // Are we !at the root
                if (Parent != null)
                {
                    // Add the new node to its parent
                    Parent.AddKey(split, newnode);
                }
                else
                {
                    // We just split the root, so make a new one
                    BPlusTreeNode root = new BPlusTreeNode(null, false, NodeSize);
                    Parent = root;
                    Parent.Keys = new List<byte[]>();
                    Parent.Keys.Add(split);
                    Parent.Children = new List<BPlusTreeNode>();
                    Parent.Children.Add(this);
                    Parent.Children.Add(newnode);
                }
            }
        }

        /// <summary>
        /// Pulls a record stored in this leaf node. If not found, returns null.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public BackupLocation GetRecord(byte[] hash)
        {
            if (IsLeafNode != true)
            {
                throw new ArgumentException("Get Record only works on interior nodes.");
            }
            for (int i = 0; i < Keys.Count; i++)
            {
                if (Keys[i].SequenceEqual(hash))
                {
                    return Values[i];
                }
            }
            return null;
        }
    }
}
