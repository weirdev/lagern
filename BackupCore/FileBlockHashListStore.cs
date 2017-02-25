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
    class FileBlockHashListStore
    {
        // TODO: Consider some inheritance relationship between this class and BPlusTree
        public BPlusTree<List<byte[]>> TreeIndexStore { get; private set; }

        public string StorePath { get; set; }

        public string BlockSaveDirectory { get; set; }

        private static int ValueHashLength = 20; // Length of hashes in hash lists

        public FileBlockHashListStore(string indexpath, string blocksavedir)
        {
            StorePath = indexpath;
            BlockSaveDirectory = blocksavedir;
            TreeIndexStore = new BPlusTree<List<byte[]>>(100);

            try
            {
                using (FileStream fs = new FileStream(indexpath, FileMode.Open, FileAccess.Read))
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
                TreeIndexStore = new BPlusTree<List<byte[]>>(100);
            }
        }

        /// <summary>
        /// Adds a hash and corresponding BackupLocation to the Index.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns>
        /// True if we already have the hash stored. False if we need to
        /// save the corresponding block.
        /// </returns>
        public bool AddHashList(byte[] hash, List<byte[]> hashlist)
        {
            // Adds a hash and Backup Location to the BlockHashStore
            return TreeIndexStore.AddHash(hash, hashlist);
        }

        public bool ContainsHash(byte[] hash)
        {
            if (TreeIndexStore.GetRecord(hash) != null)
            {
                return true;
            }
            return false;
        }

        public List<byte[]> GetHashList(byte[] hash)
        {
            return TreeIndexStore.GetRecord(hash);
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bptdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // keysize = BitConverter.GetBytes(int) (only used for decoding HashBLocationPairs)
            // HashBLocationPairs = enum_encode(List<byte[]> [hash,... & backuplocation.serialize(),...])

            bptdata.Add("keysize-v1", BitConverter.GetBytes(20));

            List<byte[]> binkeyvals = new List<byte[]>();
            foreach (KeyValuePair<byte[], List<byte[]>> kvp in TreeIndexStore)
            {
                byte[] keybytes = kvp.Key;
                byte[] hashlistbytes = new byte[kvp.Value.Count * ValueHashLength];
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    Array.Copy(kvp.Value[i], 0, hashlistbytes, i * ValueHashLength, ValueHashLength);
                }
                byte[] binkeyval = new byte[keybytes.Length + hashlistbytes.Length];
                Array.Copy(keybytes, binkeyval, keybytes.Length);
                Array.Copy(hashlistbytes, 0, binkeyval, keybytes.Length, hashlistbytes.Length);
                binkeyvals.Add(binkeyval);
            }
            bptdata.Add("HashBLocationPairs-v1", BinaryEncoding.enum_encode(binkeyvals));

            return BinaryEncoding.dict_encode(bptdata);
        }

        public void deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            int keysize = BitConverter.ToInt32(savedobjects["keysize-v1"], 0);

            foreach (byte[] binkvp in BinaryEncoding.enum_decode(savedobjects["HashBLocationPairs-v1"]))
            {
                byte[] keybytes = new byte[keysize];
                byte[] hashlistbytes = new byte[binkvp.Length - keysize];
                Array.Copy(binkvp, keybytes, keysize);
                Array.Copy(binkvp, keysize, hashlistbytes, 0, binkvp.Length - keysize);
                List<byte[]> hashlist = new List<byte[]>();
                for (int i = 0; i < hashlistbytes.Length / ValueHashLength; i++)
                {
                    byte[] hash = new byte[ValueHashLength];
                    Array.Copy(hashlistbytes, i * ValueHashLength, hash, 0, ValueHashLength);
                    hashlist.Add(hash);
                }
                this.AddHashList(keybytes, hashlist);
            }
        }

        public void SynchronizeCacheToDisk()
        {
            using (FileStream fs = new FileStream(StorePath, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(this.serialize());
                }
            }
        }
    }
}
