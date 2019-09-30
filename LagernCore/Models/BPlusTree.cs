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
    public class BPlusTree<T> : IDictionary<byte[], T>, ICollection<KeyValuePair<byte[], T>> where T : class
    {
        private BPlusTreeNode<T> Root { get; set; }

        // Head of linked list allowing for efficient in order traversal of leaf nodes
        private BPlusTreeNode<T> Head { get; set; }

        public int NodeSize { get; private set; }

        public string NodeStorePath { get; private set; }
        
        public ICollection<byte[]> Keys
        {
            get => new List<byte[]>(this.Select(kvp => kvp.Key));
        }

        public ICollection<T> Values
        {
            get => new List<T>(this.Select(kvp => kvp.Value));
        }

        public int Count { get; set; }

        bool ICollection<KeyValuePair<byte[], T>>.IsReadOnly => false;

        /// <summary>
        /// Constructor for fully in-memory tree. If saving to disk
        /// is desired, specify nodestorepath to use the other constructor.
        /// </summary>
        /// <param name="nodesize"></param>
        #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public BPlusTree(int nodesize)
        #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            NodeSize = nodesize;
            Initialize();
        }


        // Initialize the tree with existing data
        // Bulk loads data
        #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        public BPlusTree(IEnumerable<KeyValuePair<byte[], T>> hashValPairs, int nodesize)
        #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            NodeSize = nodesize;
            Initialize();
            BPlusTreeNode<T> tail = Head;
            if (tail.Values == null)
            {
                throw new Exception("Should only be iterating over leaf nodes");
            }
            foreach (var hvp in hashValPairs)
            {
                Count++;
                tail.Keys.Add(hvp.Key);
                tail.Values.Add(hvp.Value);
                if (tail.Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
                {
                    tail = SplitLeafNode(tail);
                    if (tail.Values == null)
                    {
                        throw new Exception("Should only be iterating over leaf nodes");
                    }
                }
            }
        }

        private void Initialize()
        {
            BPlusTreeNode<T> root = new BPlusTreeNode<T>(null, true, NodeSize);
            Root = root;
            Head = Root;
            Count = 0;
        }

        public void Add(byte[] hash, T value) => AddOrFind(hash, value);

        public void Add(KeyValuePair<byte[], T> keyValuePair) => Add(keyValuePair.Key, keyValuePair.Value);

        public bool ContainsKey(byte[] hash) => GetRecord(hash) != null;

        public bool Contains(KeyValuePair<byte[], T> keyValuePair) => Contains(keyValuePair.Key, keyValuePair.Value);
        public bool Contains(byte[] hash, T value) => GetRecord(hash) == value;

        /// <summary>
        /// Adds a hash and backuplocation to the tree
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blocation"></param>
        /// <returns>T if hash already exists in tree, null otherwise.</returns>
        public T? AddOrFind(byte[] hash, T blocation)
        {
            lock (this)
            {
                // Traverse down the tree
                BPlusTreeNode<T> node = FindLeafNode(hash);
                T? dosave = AddKeyToNode(node, hash, blocation);
                return dosave;
            }
        }

        // Supressing warning because the out parameter in IDictionary.TryGetValue is not marked nullable
        #pragma warning disable CS8614 // Nullability of reference types in type of parameter doesn't match implicitly implemented member.
        public bool TryGetValue(byte[] hash, out T? value)
        #pragma warning restore CS8614 // Nullability of reference types in type of parameter doesn't match implicitly implemented member.
        {
            value = GetRecord(hash);
            return value != null;
        }

        public T this[byte[] hash]
        {
            get 
            { 
                T? record = GetRecord(hash);
                if (record != null)
                {
                    return record;
                }
                throw new KeyNotFoundException("Hash does not exist in this node");
            }
            set
            {
                AddKeyToNode(FindLeafNode(hash), hash, value);
            }
        }

        public void Clear() => Initialize();

        private T? AddKeyToNode(BPlusTreeNode<T> node, byte[] hash, T blocation, bool replace = false) => 
            AddKeyToNode(node, hash, blocation, ref node, replace);
                
        private T? AddKeyToNode(BPlusTreeNode<T> node, byte[] hash, T blocation, ref BPlusTreeNode<T> tail, bool replace = false)
        {
            if (node.IsLeafNode != true || node.Values == null)
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
                if (replace)
                {
                    node.Values[position] = blocation;
                }
                return node.Values[position];
            }
            // Hash not in tree, belongs in position
            else
            {
                Count++;
                // Go ahead and add the new key/value then split as normal
                node.Keys.Insert(position, hash);
                node.Values.Insert(position, blocation);
                // Is this node full?
                if (node.Keys.Count > (NodeSize - 1)) // Nodesize-1 for keys
                {
                    tail = SplitLeafNode(node);
                }
                return null;
            }
        }

        private BPlusTreeNode<T> SplitLeafNode(BPlusTreeNode<T> node)
        {
            if (node.Parent == null) // node=Root is leaf (only node)
            {
                // Create new left node root
                BPlusTreeNode<T> newroot = new BPlusTreeNode<T>(null, false, NodeSize);
                Root = newroot;
                node.Parent = Root;
                if (Root.Children == null)
                {
                    throw new Exception("Root should now be non-leaf node");
                }
                Root.Children.Add(node); // Dont add key (Key added below, should always have one more child than key)
            }

            if (node.Values == null)
            {
                throw new Exception("Node must be leaf node");
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
            return newnode;
        }

        private void AddKeyToNode(BPlusTreeNode<T> node, byte[] hash, BPlusTreeNode<T> child)
        {
            if (node.IsLeafNode == true || node.Children == null)
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
                    if (root.Children == null)
                    {
                        throw new Exception("Root should be interior node");
                    }

                    node.Parent = root;
                    newnode.Parent = root;
                    root.Keys.Add(split);
                    root.Children.Add(node);
                    root.Children.Add(newnode);
                    Root = root;
                }
            }
        }

        public bool Remove(byte[] hash)
        {
            lock (this)
            {
            // indexing may be off here
            Stack<int> parentpositions = new Stack<int>();
            BPlusTreeNode<T> node = Root;
            while (!node.IsLeafNode)
            {
                int position = 0;
                for (; position < node.Keys.Count && !HashTools.ByteArrayLessThan(hash, node.Keys[position]); position++) { }
                if (node.Children == null)
                {
                    throw new Exception("node should be interior node here");
                }
                node = node.Children[position];
                parentpositions.Push(position);
            }
            bool found = false;
                // Leaf node reached, remove key and value
            if (node.Values == null)
            {
                throw new Exception("Node should be leaf node here");
            }
            for (int i = 0; i < node.Keys.Count; i++)
            {
                if (node.Keys[i].SequenceEqual(hash))
                {
                    found = true;
                    Count--;
                    node.Keys.RemoveAt(i);
                    node.Values.RemoveAt(i);
                    break;
                }
            }
            if (!found)
            {
                return false;
            }
            if (node.Parent != null && node.Keys.Count < NodeSize / 2) // Not at root && too small
            {
                // All leaf nodes always at same depth
                int parentpos = parentpositions.Pop();
                if (node.Parent.Children == null)
                {
                    throw new Exception("Node's parent must be interior node");
                }

                if (parentpos - 1 >= 0 && node.Parent.Children[parentpos - 1].Keys.Count > NodeSize / 2) // left neighbor more than half full?
                {
                    var lneighbor = node.Parent.Children[parentpos - 1];
                    if (lneighbor.Values == null)
                    {
                        throw new Exception("Sibling of this node should also be a leaf node");
                    }
                    // Steal entry from left neighbor
                    node.Keys.Insert(0, lneighbor.Keys[lneighbor.Keys.Count - 1]);
                    node.Values.Insert(0, lneighbor.Values[lneighbor.Keys.Count - 1]);
                    lneighbor.Keys.RemoveAt(lneighbor.Keys.Count - 1);
                    lneighbor.Values.RemoveAt(lneighbor.Values.Count - 1);
                    // Update split point above
                    node.Parent.Keys[parentpos - 1] = node.Keys[0];
                }
                else if (parentpos < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count > NodeSize / 2) // right neighbor more than half full?s
                {
                    var rneighbor = node.Parent.Children[parentpos + 1];
                    if (rneighbor.Values == null)
                    {
                        throw new Exception("Sibling of this node should also be a leaf node");
                    }
                    // Steal entry from right neighbor
                    node.Keys.Add(rneighbor.Keys[0]);
                    node.Values.Add(rneighbor.Values[0]);
                    rneighbor.Keys.RemoveAt(0);
                    rneighbor.Values.RemoveAt(0);
                    // Update split point in node above
                    node.Parent.Keys[parentpos] = rneighbor.Keys[0];
                }
                else if (parentpos - 1 >= 0) //  && node.Parent.Children[parentpos - 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with left neighbor
                    for (int i=0; i<node.Keys.Count; i++)
                    {
                        node.Parent.Children[parentpos - 1].Keys.Add(node.Keys[i]);
                        var sibnode = node.Parent.Children[parentpos - 1].Values;
                        if (sibnode == null)
                        {
                            throw new Exception("Must be a leaf node");
                        }
                        sibnode.Add(node.Values[i]);
                    }
                    node.Parent.Children[parentpos - 1].Next = node.Next;
                    node.Parent.Children[parentpos] = node.Parent.Children[parentpos - 1]; // More efficient to add entries to left node, but we will keep the node at parentpos, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos - 1, parentpositions);
                }
                else // if (parentpos < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    var sibnodevals = node.Parent.Children[parentpos + 1].Values;
                    if (sibnodevals == null)
                    {
                        throw new Exception("Must be leaf node");
                    }
                    // Merge with right neighbor
                    for (int i = 0; i < node.Parent.Children[parentpos + 1].Keys.Count; i++)
                    {
                        node.Keys.Add(node.Parent.Children[parentpos + 1].Keys[i]);
                        node.Values.Add(sibnodevals[i]);
                    }
                    node.Next = node.Parent.Children[parentpos + 1].Next;
                    node.Parent.Children[parentpos + 1] = node; // More efficient to add entries to left node, but we will keep the node at parentpos + 1, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos, parentpositions);
                }
            }
            return true;
                }
        }

        public bool Remove(KeyValuePair<byte[], T> keyValuePair) => Remove(keyValuePair.Key);

        public void CopyTo(KeyValuePair<byte[], T>[] array, int dstindex)
        {
            foreach (var kvp in this)
            {
                array[dstindex] = kvp;
                dstindex++;
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
            if (node.Children == null)
            {
                throw new Exception("Node must be internal node");
            }
            node.Children.RemoveAt(keypos);
            if (node.Parent == null) // At root?
            {
                if (node.Keys.Count == 0) // Root only has one child
                {
                    Root = node.Children[0]; // Single child becomes root
                    Root.Parent = null;
                }
            }
            else if (node.Keys.Count < NodeSize / 2) // too small ?
            {
                int parentpos = parentpositions.Pop();
                var siblings = node.Parent.Children;
                if (siblings == null)
                {
                    throw new Exception("Parent must be internal node");
                }
                if (parentpos - 1 >= 0 && siblings[parentpos - 1].Keys.Count > NodeSize / 2) // left neighbor more than half full?
                {
                    // Steal entry from left neighbor
                    // New node key is the old split point between it and its left sibling
                    // New split point between siblings is old rightmost key in left sibling
                    node.Keys.Insert(0, node.Parent.Keys[parentpos - 1]);
                    var nephews = siblings[parentpos - 1].Children;
                    if (nephews == null)
                    {
                        throw new Exception("Siblings must be internal nodes");
                    }
                    node.Children.Insert(0, nephews[nephews.Count - 1]);
                    node.Parent.Keys[parentpos - 1] = siblings[parentpos - 1].Keys[siblings[parentpos - 1].Keys.Count - 1];
                    siblings[parentpos - 1].Keys.RemoveAt(siblings[parentpos - 1].Keys.Count - 1);
                    nephews.RemoveAt(nephews.Count - 1);
                }
                else if (parentpos < node.Parent.Keys.Count && siblings[parentpos + 1].Keys.Count > NodeSize / 2) // right neighbor more than half full?s
                {
                    // Steal entry from right neighbor
                    node.Keys.Add(node.Parent.Keys[parentpos]);
                    var nephews = siblings[parentpos + 1].Children;
                    if (nephews == null)
                    {
                        throw new Exception("Siblings must be internal nodes");
                    }

                    node.Children.Add(nephews[0]);
                    node.Parent.Keys[parentpos] = siblings[parentpos + 1].Keys[0];
                    siblings[parentpos + 1].Keys.RemoveAt(0);
                    nephews.RemoveAt(0);
                }
                else if (parentpos - 1 >= 0) //  && node.Parent.Children[parentpos - 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with left neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    siblings[parentpos - 1].Keys.Add(node.Parent.Keys[parentpos - 1]);
                    for (int i = 0; i < node.Keys.Count; i++)
                    {
                        siblings[parentpos - 1].Keys.Add(node.Keys[i]);
                    }
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        var nephews = siblings[parentpos - 1].Children;
                        if (nephews == null)
                        {
                            throw new Exception("Siblings must be internal nodes");
                        }
                        nephews.Add(node.Children[i]);
                    }
                    siblings[parentpos] = siblings[parentpos - 1]; // More efficient to add entries to left node, but we will keep the node at parentpos, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos - 1, parentpositions);
                }
                else // if (parentpos < node.Parent.Keys.Count && node.Parent.Children[parentpos + 1].Keys.Count == NodeSize / 2) (must be true or invariant already violated)
                {
                    // Merge with right neighbor
                    // Pull down key from parent (inverse of bubbling up key on a split)
                    node.Keys.Add(node.Parent.Keys[parentpos]);
                    for (int i = 0; i < siblings[parentpos + 1].Keys.Count; i++)
                    {
                        node.Keys.Add(siblings[parentpos + 1].Keys[i]);
                    }
                    var nephews = siblings[parentpos + 1].Children;
                    if (nephews == null)
                    {
                        throw new Exception("Siblings must be internal nodes");
                    }
                    for (int i = 0; i < nephews.Count; i++)
                    {
                        node.Children.Add(nephews[i]);
                    }
                    siblings[parentpos + 1] = node; // More efficient to add entries to left node, but we will keep the node at parentpos + 1, so update parents reference
                    RemoveInternalNodeEntry(node.Parent, parentpos, parentpositions);
                }
            }
        }

        private T? GetRecordFromNode(BPlusTreeNode<T> node, byte[] hash)
        {
            if (node.IsLeafNode != true || node.Values == null)
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

        private BPlusTreeNode<T> FindLeafNode(byte[] hash)
        {
            // Traverse down the tree
            BPlusTreeNode<T> node = Root;
            while (!node.IsLeafNode && node.Children != null)
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
                    if (!node.IsLeafNode && node.Children != null)
                    {
                        foreach (var child in node.Children)
                        {
                            printqueue.Enqueue(child);
                        }
                    }
                }
            }

        }

        public T? GetRecord(byte[] hash)
        {
            lock (this)
            {
                return GetRecordFromNode(FindLeafNode(hash), hash);
            }
        }
        
        public IEnumerator<KeyValuePair<byte[], T>> GetEnumerator()
        {
            BPlusTreeNode<T>? node = Head;
            while (node != null)
            {
                if (node.Values == null)
                {
                    throw new Exception("Should only be at leaf nodes");
                }
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
    }
}
