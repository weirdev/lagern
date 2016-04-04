﻿using System;
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
        }

        public bool AddKey(byte[] hash, BackupLocation value)
        {
            if (IsLeafNode != true)
            {
                throw new ArgumentException("A child node must be specified with the key" 
                    + " if the node to be added to is an interior node.");
            }
            // Look for key in node
            int position = 0;
            for (; !HashTools.ByteArrayLessThan(Keys[position], hash); position++) { }
            // Hash already exists in BPlusTree, return false (saving data block not necessary)
            if (Keys[position] == hash)
            {
                return false;
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
                    Values.RemoveRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                    // Add the new node to its parent
                    Parent.AddKey(newnode.Keys[0], newnode);
                }
                return true;
            }
        }

        public void AddKey(byte[] hash, BPlusTreeNode child)
        {
            if (IsLeafNode == true)
            {
                throw new ArgumentException("A value must be specified with the key"
                    + " if the node to be added to is a leaf node.");
            }
            // Look for where to put key in node
            int position = 0;
            for (; !HashTools.ByteArrayLessThan(Keys[position], hash); position++) { }
            // Key "can't" already be in node because it was split from a lower node
            Keys.Insert(position, hash);
            Children.Insert(position + 1, child);
            // Is this node full?
            if (Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
            {
                // Create a new node and add half of this node's keys/ values to it
                BPlusTreeNode newnode = new BPlusTreeNode(Parent, true, NodeSize);
                newnode.Keys = Keys.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                newnode.Values = Values.GetRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                Keys.RemoveRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
                Values.RemoveRange(Keys.Count / 2, Keys.Count - (Keys.Count / 2));
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
    }
}