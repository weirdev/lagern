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
    public class BPlusTree : IEnumerable<KeyValuePair<byte[], BackupLocation>>, ICustomSerializable<BPlusTree>
    {

        private BPlusTreeNode Root { get; set; }

        // Head of linked list allowing for efficient in order traversal of leaf nodes
        private BPlusTreeNode Head { get; set; }

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

        public BPlusTree(int nodesize, string nodestorepath) : this(nodesize)
        {
            NodeStorePath = nodestorepath;

            try
            {
                using (FileStream fs = new FileStream(nodestorepath, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        this.deserialize(reader.ReadBytes((int)fs.Length));
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Reading old index failed. Initializing new index...");
            }
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
                    List<byte[]> oldkeys = new List<byte[]>(node.Keys);
                    List<BackupLocation> oldvalues = new List<BackupLocation>(node.Values);
                    newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    newnode.Values = new ObservableCollection<BackupLocation>(oldvalues.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                    oldkeys.RemoveRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2));
                    node.Keys = new ObservableCollection<byte[]>(oldkeys);
                    oldvalues.RemoveRange(node.Values.Count / 2, node.Values.Count - (node.Values.Count / 2));
                    node.Values = new ObservableCollection<BackupLocation>(oldvalues);
                    // Add the new node to its parent
                    AddKeyToNode(node.Parent, newnode.Keys[0], newnode);
                }
                return false;
            }
        }

        private void AddKeyToNode(BPlusTreeNode node, byte[] hash, BPlusTreeNode child)
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
                List<BPlusTreeNode> oldchildren = new List<BPlusTreeNode>(node.Children);
                newnode.Keys = new ObservableCollection<byte[]>(oldkeys.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2)));
                newnode.Children = new ObservableCollection<BPlusTreeNode>(oldchildren.GetRange(node.Keys.Count / 2, node.Keys.Count - (node.Keys.Count / 2) + 1));
                int keycount = node.Keys.Count;
                oldkeys.RemoveRange(keycount / 2, keycount - (keycount / 2));
                node.Keys = new ObservableCollection<byte[]>(oldkeys);
                oldchildren.RemoveRange(keycount / 2, keycount - (keycount / 2) + 1);
                node.Children = new ObservableCollection<BPlusTreeNode>(oldchildren);
                byte[] split = node.Keys[node.Keys.Count - 1];
                node.Keys.RemoveAt(node.Keys.Count - 1);

                // Make all newly split out Children refer to newnode as their parent
                // TODO: This is very inefficient when reading all the children to/from disk
                foreach (var nchild in newnode.Children)
                {
                    BPlusTreeNode ri_nchild = nchild;
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
                    BPlusTreeNode root = new BPlusTreeNode(null, false, NodeSize);
                    node.Parent = root;
                    newnode.Parent = root;
                    root.Keys.Add(split);
                    root.Children.Add(node);
                    root.Children.Add(newnode);
                    Root = root;
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

        public void SynchronizeCacheToDisk()
        {
            using (FileStream fs = new FileStream(NodeStorePath, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(this.serialize());
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

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bptdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // keysize = BitConverter.GetBytes(int) (only used for decoding HashBLocationPairs)
            // HashBLocationPairs = enum_encode(List<byte[]> [hash,... & backuplocation.serialize(),...])

            bptdata.Add("keysize-v1", BitConverter.GetBytes(20));

            List<byte[]> binkeyvals = new List<byte[]>();
            foreach (KeyValuePair<byte[], BackupLocation> kvp in this)
            {
                byte[] keybytes = kvp.Key;
                byte[] backuplocationbytes = kvp.Value.serialize();
                byte[] binkeyval = new byte[keybytes.Length + backuplocationbytes.Length];
                Array.Copy(keybytes, binkeyval, keybytes.Length);
                Array.Copy(backuplocationbytes, 0, binkeyval, keybytes.Length, backuplocationbytes.Length);
                binkeyvals.Add(binkeyval);
            }
            bptdata.Add("HashBLocationPairs-v1", BinaryEncoding.enum_encode(binkeyvals));

            return BinaryEncoding.dict_encode(bptdata);
        }

        public void deserialize(byte[] data)
        {
            this.Initialize();
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            int keysize = BitConverter.ToInt32(savedobjects["keysize-v1"], 0);

            foreach (byte[] binkvp in BinaryEncoding.enum_decode(savedobjects["HashBLocationPairs-v1"]))
            {
                byte[] keybytes = new byte[keysize];
                byte[] backuplocationbytes = new byte[binkvp.Length - keysize];
                Array.Copy(binkvp, keybytes, keysize);
                Array.Copy(binkvp, keysize, backuplocationbytes, 0, binkvp.Length - keysize);

                this.AddHash(keybytes, BackupLocation.deserialize(backuplocationbytes));
            }
        }

        private void Initialize()
        {

            BPlusTreeNode root = new BPlusTreeNode(null, false, NodeSize);
            Root = root;
            BPlusTreeNode rootchild2 = new BPlusTreeNode(Root, true, NodeSize);
            BPlusTreeNode rootchild1 = new BPlusTreeNode(Root, true, NodeSize, rootchild2);
            root.Children.Add(rootchild1);
            root.Children.Add(rootchild2);
            root.Keys.Add(HashTools.HexStringToByteArray("8000000000000000000000000000000000000000"));

            Head = rootchild1;
        }
    }
}
