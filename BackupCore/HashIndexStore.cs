using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    /// <summary>
    /// Binary tree holding hashes and their corresponding locations in backup
    /// </summary>
    class HashIndexStore
    {
        public BTNode<IMemberFilter<byte[]>, HashBin> Root { get; private set; }

        public HashIndexStore()
        {
            Root = new BTNode<IMemberFilter<byte[]>, HashBin>();
            Root.InteriorValue = new CukooFilter();

            Root.LeftChild = new BTNode<IMemberFilter<byte[]>, HashBin>();
            Root.LeftChild.LeafValue = new HashBin();
            Root.RightChild = new BTNode<IMemberFilter<byte[]>, HashBin>();
            Root.RightChild.LeafValue = new HashBin();
        }
        
        /// <summary>
        /// Adds a hash and corresponding BackupLocation to the Index.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns>
        /// True if we already have the hash stored. False if we need to
        /// save the corresponding block.
        /// </returns>
        public bool AddHash(byte[] hash, BackupLocation blocation)
        {
            // Adds a hash and Backup Location to the HashIndexStore
            if (ContainsHash(hash, Root)) { return true; }
            else
            {
                SaveNewHash(hash, blocation);
                return false;
            }
        }

        private void SaveNewHash(byte[] hash, BackupLocation blocation)
        {
            // Find "highest" leaf node
            BTNode<IMemberFilter<byte[]>, HashBin> leafnode = Root;
            Queue<BTNode<IMemberFilter<byte[]>, HashBin>> searchqueue = new Queue<BTNode<IMemberFilter<byte[]>, HashBin>>();
            Random rand = new Random();
            while (leafnode.LeafValue == null)
            {
                // This is important so that bins are filled
                if (rand.NextDouble() > 0.5)
                {
                    searchqueue.Enqueue(leafnode.LeftChild);
                    searchqueue.Enqueue(leafnode.RightChild);
                }
                else
                {
                    searchqueue.Enqueue(leafnode.RightChild);
                    searchqueue.Enqueue(leafnode.LeftChild);
                }
                leafnode = searchqueue.Dequeue();
            }
            // Try save, 
            if (leafnode.LeafValue.AddHash(hash, blocation))
            {
                
            }
            // If leaf is full, allocate 2 new nodes.
            // Each new node will hold half of the original hashbin from the leaf
            // Leaf is then converted to an interior node with a new filter
            // Since we just pushed this node deeper, recurse and run SaveNewHash
            // again with same parameter
            else
            {
                BTNode<IMemberFilter<byte[]>, HashBin> newleftnode = new BTNode<IMemberFilter<byte[]>, HashBin>();
                BTNode<IMemberFilter<byte[]>, HashBin> newrightnode = new BTNode<IMemberFilter<byte[]>, HashBin>();
                newleftnode.LeafValue = leafnode.LeafValue;
                newrightnode.LeafValue = newleftnode.LeafValue.TakeHalf();

                leafnode.LeafValue = null;
                leafnode.LeftChild = newleftnode;
                leafnode.RightChild = newrightnode;
                leafnode.InteriorValue = new CukooFilter(leafnode.LeftChild.LeafValue, leafnode.RightChild.LeafValue);
                SaveNewHash(hash, blocation);
            }
        }

        public bool ContainsHash(byte[] hash, BTNode<IMemberFilter<byte[]>, HashBin> searchNode)
        {
            // Interior node = Filter
            if (searchNode.InteriorValue != null)
            {
                if (searchNode.InteriorValue.ContainsItem(hash))
                {
                    return ContainsHash(hash, searchNode.LeftChild) || ContainsHash(hash, searchNode.RightChild);
                }
                return false;
            }
            // Leaf node = HashBin
            else
            {
                return searchNode.LeafValue.ContainsHash(hash);
            }
        }

        public BackupLocation GetBackupLocation(byte[] hash)
        {
            Queue<BTNode<IMemberFilter<byte[]>, HashBin>> searchqueue = new Queue<BTNode<IMemberFilter<byte[]>, HashBin>>();
            searchqueue.Enqueue(Root);
            while (searchqueue.Count != 0)
            {
                BTNode<IMemberFilter<byte[]>, HashBin> node = searchqueue.Dequeue();
                // Interior node = check filter, add children if match
                if (node.InteriorValue != null)
                {
                    if (node.InteriorValue.ContainsItem(hash))
                    {
                        searchqueue.Enqueue(node.LeftChild);
                        searchqueue.Enqueue(node.RightChild);
                    }
                }
                // Leaf node = search directly
                else
                {
                    BackupLocation bl = node.LeafValue.GetBackupLocation(hash);
                    if (bl != null)
                    {
                        return bl;
                    }
                }
            }
            return null;
        }
    }
}
