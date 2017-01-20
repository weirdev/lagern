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
        // TODO: Consider some inheritance relationship between this class and BPlusTree
        public BPlusTree TreeIndexStore { get; private set; }

        public string IndexPath { get; set; }

        public HashIndexStore(string indexpath)
        {
            IndexPath = indexpath;
            TreeIndexStore = new BPlusTree(100, IndexPath);
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
            return TreeIndexStore.AddHash(hash, blocation);
        }

        public bool ContainsHash(byte[] hash)
        {
            if (TreeIndexStore.GetRecord(hash) != null)
            {
                return true;
            }
            return false;
        }

        public BackupLocation GetBackupLocation(byte[] hash)
        {
            return TreeIndexStore.GetRecord(hash);
        }

        public void SynchronizeCacheToDisk()
        {
            TreeIndexStore.SynchronizeCacheToDisk();
        }
    }
}
