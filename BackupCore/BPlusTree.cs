using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Collections.ObjectModel;

namespace BackupCore
{
    class BPlusTree : IEnumerable<KeyValuePair<byte[], BackupLocation>>
    {
        protected BPTNodeStore NodeCache { get; set; }

        public BPlusTreeNode Root { get; set; }

        // Head of linked list allowing for efficient in order traversal of leaf nodes
        private BPlusTreeNode Head { get; set; }

        public int NodeSize { get; private set; }

        public string NodeStorePath { get; private set; }

        public BPlusTree(int nodesize, string nodestorepath)
        {
            NodeStorePath = nodestorepath;
            NodeCache = new BPTNodeStore(NodeStorePath);

            NodeSize = nodesize;
            Root = new BPlusTreeNode(null, false, NodeSize);
            BPlusTreeNode rootchild2 = new BPlusTreeNode(Root.NodeID, true, NodeSize);
            BPlusTreeNode rootchild1 = new BPlusTreeNode(Root.NodeID, true, NodeSize, rootchild2.NodeID);
            Root.Children.Add(rootchild1.NodeID);
            Root.Children.Add(rootchild2.NodeID);
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
                Root = NodeCache.GetNode(Root.Parent);
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
                    node.Next = newnode.NodeID;
                    List<byte[]> oldkeys = new List<byte[]>(node.Keys);
                    List<BackupLocation> oldvalues = new List<BackupLocation>(node.Values);
                    newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    newnode.Values = new ObservableCollection<BackupLocation>(oldvalues.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    oldkeys.RemoveRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    node.Keys = new ObservableCollection<byte[]>(oldkeys);
                    oldvalues.RemoveRange(node.Values.Count / 2, node.Values.Count - (node.Values.Count / 2));
                    node.Values = new ObservableCollection<BackupLocation>(oldvalues);
                    // Add the new node to its parent
                    AddKeyToNode(NodeCache.GetNode(node.Parent), newnode.Keys[0], newnode.NodeID);
                }
                return false;
            }
        }

        public void AddKeyToNode(BPlusTreeNode node, byte[] hash, string child)
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
                List<byte[]> oldkeys = new List<byte[]>(node.Keys);
                List<string> oldchildren = new List<string>(node.Children);
                newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                newnode.Children = new ObservableCollection<string>(oldchildren.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2) + 1));
                int keycount = node.Keys.Count;
                oldkeys.RemoveRange(keycount / 2, keycount - (keycount / 2));
                node.Keys = new ObservableCollection<byte[]>(oldkeys);
                oldchildren.RemoveRange(keycount / 2, keycount - (keycount / 2) + 1);
                node.Children = new ObservableCollection<string>(oldchildren);
                byte[] split = node.Keys[node.Keys.Count - 1];
                node.Keys.RemoveAt(node.Keys.Count - 1);

                // Make all newly split out Children refer to newnode as their parent
                // TODO: This is very inefficient when reading all the children to/from disk
                foreach (var nchild in newnode.Children)
                {
                    BPlusTreeNode ri_nchild = NodeCache.GetNode(nchild);
                    ri_nchild.Parent = newnode.NodeID;
                }

                // Are we !at the root
                if (node.Parent != null)
                {
                    // Add the new node to its parent
                    AddKeyToNode(NodeCache.GetNode(node.Parent), split, newnode.NodeID);
                }
                else
                {
                    // We just split the root, so make a new one
                    BPlusTreeNode root = new BPlusTreeNode(null, false, NodeSize);
                    node.Parent = root.NodeID;
                    root.Keys.Add(split);
                    root.Children.Add(node.NodeID);
                    root.Children.Add(newnode.NodeID);
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
                node = NodeCache.GetNode(node.Children[child]);
            }
            return node;
        }

        public void SynchronizeCacheToDisk()
        {
            NodeCache.SynchronizeToDisk();
        }

        public static void SerializeNode(BPlusTreeNode node, string writepath)
        {
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.WriteEndDocumentOnClose = true;
            settings.Indent = true;
            settings.NewLineOnAttributes = true;
            settings.IndentChars = "    ";

            DataContractSerializer metaserializer = new DataContractSerializer(typeof(BPlusTreeNode));
            using (XmlWriter writer = XmlWriter.Create(writepath, settings))
            {
                metaserializer.WriteObject(writer, node);
            }
        }

        public static BPlusTreeNode DeserializeNode(string path)
        {
            // Deserialize location dict
            using (FileStream fs = new FileStream(path, FileMode.Open))
            {
                using (XmlDictionaryReader reader =
                    XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(BPlusTreeNode));

                    // Deserialize the data and read it from the instance.
                    return (BPlusTreeNode)ser.ReadObject(reader, true);
                }
            }
        }

        private void PrintTree()
        {
            using (StreamWriter file =
            new StreamWriter(@"C:\Users\Wesley\Desktop\tree.txt", true))
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
                            printqueue.Enqueue(NodeCache.GetNode(child));
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
                node = NodeCache.GetNode(node.Next);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
