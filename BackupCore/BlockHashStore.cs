using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    /// <summary>
    /// Binary tree holding hashes and their corresponding locations in backup
    /// </summary>
    class BlockHashStore
    {
        // TODO: Consider some inheritance relationship between this class and BPlusTree
        public BPlusTree TreeIndexStore { get; private set; }

        public string IndexPath { get; set; }

        public string BlockSaveDirectory { get; set; }

        public BlockHashStore(string indexpath, string blocksavedir)
        {
            IndexPath = indexpath;
            BlockSaveDirectory = blocksavedir;
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
            // Adds a hash and Backup Location to the BlockHashStore
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

        public byte[] ReconstructFileData(List<byte[]> blockhashes)
        {
            MemoryStream file = new MemoryStream();
            foreach (var hash in blockhashes)
            {
                BackupLocation blockloc = GetBackupLocation(hash);
                FileStream blockstream = File.OpenRead(Path.Combine(BlockSaveDirectory, blockloc.RelativeFilePath));
                byte[] buffer = new byte[blockstream.Length];
                blockstream.Read(buffer, 0, blockloc.ByteLength);
                file.Write(buffer, 0, blockloc.ByteLength);
                blockstream.Close();
            }
            byte[] filedata = file.ToArray();
            file.Close();
            return filedata;
        }

        public void SynchronizeCacheToDisk()
        {
            TreeIndexStore.SynchronizeCacheToDisk();
        }
    }
}
