using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Collections.ObjectModel;

namespace BackupCore
{
    public class BPlusTree<T> : IEnumerable<KeyValuePair<byte[], T>> where T : class
    {
        private BPlusTreeNode<T> Root { get; set; }

        // Head of linked list allowing for efficient in order traversal of leaf nodes
        private BPlusTreeNode<T> Head { get; set; }

        public int NodeSize { get; private set; }

        public string NodeStorePath { get; private set; }

        /// <summary>
        /// Constructor for fully in-memory tree. If saving to disk
        /// is desired, specify nodestorepath to use the other constructor.
        /// </summary>
        /// <param name="nodesize"></param>
        public BPlusTree(int nodesize)
        {
            Initialize();
            NodeSize = nodesize;
        }

        /// <summary>
        /// Adds a hash and backuplocation to the tree
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blocation"></param>
        /// <returns>T if hash already exists in tree, null otherwise.</returns>
        public T AddHash(byte[] hash, T blocation)
        {
            // Traverse down the tree
            BPlusTreeNode<T> node = FindLeafNode(hash);
            T dosave = AddKeyToNode(node, hash, blocation);
            return dosave;
        }

        private T AddKeyToNode(BPlusTreeNode<T> node, byte[] hash, T blocation)
        {
            if (node.IsLeafNode != true)
            {
                throw new ArgumentException("A child node must be specified with the key"
                    + " if the node to be added to is an interior node.");
            }
            // Look for key in node
            int position = 0;
            for (; position < node.Keys.Count && !HashTools.ByteArrayLessThanEqualTo(hash, node.Keys[position]); position++) { }
            // Hash already exists in BPlusTree, return value
            if (position < node.Keys.Count && node.Keys[position].SequenceEqual(hash))
            {
                return node.Values[position];
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
                    if (node.Parent == null) // node=Root is leaf (only node)
                    {
                        // Create new left node root
                        BPlusTreeNode<T> newroot = new BPlusTreeNode<T>(null, false, NodeSize);
                        Root = newroot;
                        node.Parent = Root;
                        Root.Children.Add(node); // Dont add key (Key added below, should always have one more child than key)
                    }
                    // Create a new node and add half of this node's keys/ values to it
                    BPlusTreeNode<T> newnode = new BPlusTreeNode<T>(node.Parent, true, NodeSize, node.Next);
                    node.Next = newnode;
                    List<byte[]> oldkeys = new List<byte[]>(node.Keys);
                    List<T> oldvalues = new List<T>(node.Values);
                    newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    newnode.Values = new ObservableCollection<T>(oldvalues.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    oldkeys.RemoveRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    node.Keys = new ObservableCollection<byte[]>(oldkeys);
                    oldvalues.RemoveRange(node.Values.Count / 2, node.Values.Count - (node.Values.Count / 2));
                    node.Values = new ObservableCollection<T>(oldvalues);
                    // Add the new node to its parent
                    AddKeyToNode(node.Parent, newnode.Keys[0], newnode);
                }
                return null;
            }
        }

        private void AddKeyToNode(BPlusTreeNode<T> node, byte[] hash, BPlusTreeNode<T> child)
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
                BPlusTreeNode<T> newnode = new BPlusTreeNode<T>(node.Parent, false, NodeSize);
                List<byte[]> oldkeys = new List<byte[]>(node.Keys);
                List<BPlusTreeNode<T>> oldchildren = new List<BPlusTreeNode<T>>(node.Children);
                newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                newnode.Children = new ObservableCollection<BPlusTreeNode<T>>(oldchildren.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2) + 1));
                int keycount = node.Keys.Count;
                oldkeys.RemoveRange(keycount / 2, keycount - (keycount / 2));
                node.Keys = new ObservableCollection<byte[]>(oldkeys);
                oldchildren.RemoveRange(keycount / 2, keycount - (keycount / 2) + 1);
                node.Children = new ObservableCollection<BPlusTreeNode<T>>(oldchildren);
                byte[] split = node.Keys[node.Keys.Count - 1];
                node.Keys.RemoveAt(node.Keys.Count - 1);

                // Make all newly split out Children refer to newnode as their parent
                // TODO: This is very inefficient when reading all the children to/from disk
                foreach (var nchild in newnode.Children)
                {
                    BPlusTreeNode<T> ri_nchild = nchild;
                    ri_nchild.Parent = newnode;
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
                    BPlusTreeNode<T> root = new BPlusTreeNode<T>(null, false, NodeSize);
                    node.Parent = root;
                    newnode.Parent = root;
                    root.Keys.Add(split);
                    root.Children.Add(node);
                    root.Children.Add(newnode);
                    Root = root;
                }
            }
        }

        public void RemoveKey(byte[] hash)
        {
            // indexing may be off here
            Stack<int> parentpositions = new Stack<int>();
            BPlusTreeNode<T> node = Root;
            while (!node.IsLeafNode)
            {
                int position = 0;
                for (; position < node.Keys.Count && !HashTools.ByteArrayLessThan(hash, node.Keys[position]); position++) { }
                node = node.Children[position];
                parentpositions.Push(position);
            }
            // Leaf node reached, remove key and value
            for (int i = 0; i < node.Keys.Count; i++)
            {
                if (node.Keys[i].SequenceEqual(hash))
                {
                    node.Keys.RemoveAt(i);
                    node.Values.RemoveAt(i);
                    break;
                }
            }
            if (node.Parent != null && node.Keys.Count < NodeSize / 2) // Not at root && too small
            {
                // All leaf nodes always at same depth
                int parentpos = parentpositions.Pop();
                if (parentpos - 1 >= 0 && node.Parent.Children[parentpos - 1].Keys.Count > NodeSize / 2) // left neighbor more than half full?
                {
                    // Steal entry from left neighbor
                    node.Keys.Insert(0, node.Parent.Children[parentpos - 1].Keys[node.Parent.Children[parentpos - 1].Keys.Count - 1]);
                    node.Values.Insert(0, node.Parent.Children[parentpos - 1].Values[node.Parent.Children[parentpos - 1].Keys.Count - 1]);
                    node.Parent.Children[parentpos - 1].Keys.RemoveAt(node.Parent.Children[parentpos - 1].Keys.Count - 1);
                    node.Parent.Children[parentpos - 1].Values.RemoveAt(node.Parent.Children[parentpos - 1].Keys.Count - 1);
                    // Update split point above
                    node.Parent.Keys[parentpos - 1] = node.Keys[0];
                }
                else if (parentpos + 1 < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count > NodeSize / 2) // right neighbor more than half full?s
                {
                    // Steal entry from right neighbor
                    node.Keys.Add(node.Parent.Children[parentpos + 1].Keys[0]);
                    node.Values.Add(node.Parent.Children[parentpos + 1].Values[0]);
                    node.Parent.Children[parentpos + 1].Keys.RemoveAt(0);
                    node.Parent.Children[parentpos + 1].Values.RemoveAt(0);
                    // Update split point in node above
                    node.Parent.Keys[parentpos] = node.Parent.Children[parentpos + 1].Keys[0];
                }
                else if (parentpos - 1 >= 0) //  && node.Parent.Children[parentpos - 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with left neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    node.Parent.Children[parentpos - 1].Keys.Add(node.Parent.Keys[parentpos - 1]);
                    for (int i=0; i<node.Keys.Count; i++)
                    {
                        node.Parent.Children[parentpos - 1].Keys.Add(node.Keys[i]);
                        node.Parent.Children[parentpos - 1].Values.Add(node.Values[i]);
                    }
                    node.Parent.Children[parentpos - 1].Next = node.Next;
                    node.Parent.Children[parentpos] = node.Parent.Children[parentpos - 1]; // More efficient to add entries to left node, but we will keep the node at parentpos, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos - 1, parentpositions);
                }
                else // if (parentpos + 1 < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with right neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    node.Keys.Add(node.Parent.Keys[parentpos]);
                    for (int i = 0; i < node.Keys.Count; i++)
                    {
                        node.Keys.Add(node.Parent.Children[parentpos + 1].Keys[i]);
                        node.Values.Add(node.Parent.Children[parentpos + 1].Values[i]);
                    }
                    node.Next = node.Parent.Children[parentpos + 1].Next;
                    node.Parent.Children[parentpos + 1] = node; // More efficient to add entries to left node, but we will keep the node at parentpos + 1, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos, parentpositions);
                }
            }
        }

        /// <summary>
        /// Remove a key and the corresponding child from an internal node.
        /// May bubble up all the way to root.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="hash"></param>
        private void RemoveInternalNodeEntry(BPlusTreeNode<T> node, int keypos, Stack<int> parentpositions)
        {
            node.Keys.RemoveAt(keypos);
            node.Children.RemoveAt(keypos);
            if (node.Parent == null) // At root?
            {
                if (node.Keys.Count == 0) // Root only has one child
                {
                    Root = node.Children[0]; // Single child becomes root
                }
            }
            else if (node.Keys.Count < NodeSize / 2) // too small ?
            {
                int parentpos = parentpositions.Pop();
                if (parentpos - 1 >= 0 && node.Parent.Children[parentpos - 1].Keys.Count > NodeSize / 2) // left neighbor more than half full?
                {
                    // Steal entry from left neighbor
                    node.Keys.Insert(0, node.Parent.Children[parentpos - 1].Keys[node.Parent.Children[parentpos - 1].Keys.Count - 1]);
                    node.Children.Insert(0, node.Parent.Children[parentpos - 1].Children[node.Parent.Children[parentpos - 1].Keys.Count - 1]);
                    node.Parent.Children[parentpos - 1].Keys.RemoveAt(node.Parent.Children[parentpos - 1].Keys.Count - 1);
                    node.Parent.Children[parentpos - 1].Children.RemoveAt(node.Parent.Children[parentpos - 1].Keys.Count - 1);
                    // Update split point above
                    node.Parent.Keys[parentpos - 1] = node.Keys[0];
                }
                else if (parentpos + 1 < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count > NodeSize / 2) // right neighbor more than half full?s
                {
                    // Steal entry from right neighbor
                    node.Keys.Add(node.Parent.Children[parentpos + 1].Keys[0]);
                    node.Children.Add(node.Parent.Children[parentpos + 1].Children[0]);
                    node.Parent.Children[parentpos + 1].Keys.RemoveAt(0);
                    node.Parent.Children[parentpos + 1].Children.RemoveAt(0);
                    // Update split point in node above
                    node.Parent.Keys[parentpos] = node.Parent.Children[parentpos + 1].Keys[0];
                }
                else if (parentpos - 1 >= 0) //  && node.Parent.Children[parentpos - 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with left neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    node.Parent.Children[parentpos - 1].Keys.Add(node.Parent.Keys[parentpos - 1]);
                    for (int i = 0; i < node.Keys.Count; i++)
                    {
                        node.Parent.Children[parentpos - 1].Keys.Add(node.Keys[i]);
                        node.Parent.Children[parentpos - 1].Children.Add(node.Children[i]);
                    }
                    node.Parent.Children[parentpos] = node.Parent.Children[parentpos - 1]; // More efficient to add entries to left node, but we will keep the node at parentpos, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos - 1, parentpositions);
                }
                else // if (parentpos + 1 < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with right neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    node.Keys.Add(node.Parent.Keys[parentpos]);
                    for (int i = 0; i < node.Keys.Count; i++)
                    {
                        node.Keys.Add(node.Parent.Children[parentpos + 1].Keys[i]);
                        node.Children.Add(node.Parent.Children[parentpos + 1].Children[i]);
                    }
                    node.Parent.Children[parentpos] = node.Parent.Children[parentpos + 1]; // More efficient to add entries to left node, but we will keep the node at parentpos + 1, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos, parentpositions);
                }
            }
        }

        private T GetRecordFromNode(BPlusTreeNode<T> node, byte[] hash)
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
            return default(T);
        }

        private BPlusTreeNode<T> FindLeafNode(byte[] hash)
        {
            // Traverse down the tree
            BPlusTreeNode<T> node = Root;
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
            using (StreamWriter file =
            new StreamWriter(@"C:\Users\Wesley\Desktop\tree.txt", true))
            {
                Queue<BPlusTreeNode<T>> printqueue = new Queue<BPlusTreeNode<T>>();
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

        public T GetRecord(byte[] hash)
        {
            return GetRecordFromNode(FindLeafNode(hash), hash);
        }

        public IEnumerator<KeyValuePair<byte[], T>> GetEnumerator()
        {
            BPlusTreeNode<T> node = Head;
            while (node != null)
            {
                for (int i = 0; i < node.Keys.Count; i++)
                {
                    yield return new KeyValuePair<byte[], T>(node.Keys[i], node.Values[i]);
                }
                node = node.Next;
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private void Initialize()
        {

            BPlusTreeNode<T> root = new BPlusTreeNode<T>(null, true, NodeSize);
            Root = root;

            Head = Root;
        }
    }
}
