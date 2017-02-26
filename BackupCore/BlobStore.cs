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
    class BlobStore : ICustomSerializable<BlobStore>
    {
        // TODO: Consider some inheritance relationship between this class and BPlusTree
        public BPlusTree<BlobLocation> TreeIndexStore { get; private set; }

        public string StorePath { get; set; }

        public string BlockSaveDirectory { get; set; }

        public BlobStore(string indexpath, string blocksavedir)
        {
            StorePath = indexpath;
            BlockSaveDirectory = blocksavedir;
            TreeIndexStore = new BPlusTree<BlobLocation>(100);

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
                TreeIndexStore = new BPlusTree<BlobLocation>(100);
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
        private bool AddHash(byte[] hash, BlobLocation blocation)
        {
            // Adds a hash and Blob Location to the BlockHashStore
            BlobLocation existingblocation = TreeIndexStore.AddHash(hash, blocation);
            if (existingblocation != null)
            {
                existingblocation.ReferenceCount += 1;
            }
            return existingblocation != null;
        }

        public void AddBlob(byte[] hash, byte[] blob, BlobLocation.BlobTypes type)
        {
            string relpath = HashTools.ByteArrayToHexViaLookup32(hash);
            BlobLocation posblocation = new BlobLocation(type, relpath, 0, blob.Length);
            bool alreadystored = false;
            lock (this)
            {
                // Have we already stored this 
                alreadystored = AddHash(hash, posblocation);
            }
            if (!alreadystored)
            {
                WriteBlob(posblocation, blob);
            }
        }

        public void WriteBlob(BlobLocation blocation, byte[] blob)
        {
            string path = Path.Combine(BlockSaveDirectory, blocation.RelativeFilePath);
            using (FileStream writer = File.OpenWrite(path))
            {
                writer.Write(blob, 0, blob.Length);
                writer.Flush();
                writer.Close();
            }
        }

        public bool ContainsHash(byte[] hash)
        {
            if (TreeIndexStore.GetRecord(hash) != null)
            {
                return true;
            }
            return false;
        }

        public BlobLocation GetBackupLocation(byte[] hash)
        {
            return TreeIndexStore.GetRecord(hash);
        }

        public byte[] ReconstructFileData(byte[] filehash)
        {
            // First read the list of hashes associated with a file, then read
            // in the data associated with each hash in the list
            BlobLocation blockhashlistloc = GetBackupLocation(filehash);
            FileStream blockhashstream = File.OpenRead(Path.Combine(BlockSaveDirectory, blockhashlistloc.RelativeFilePath));
            List<byte[]> blockhashes = new List<byte[]>();
            for (int i = 0; i < blockhashstream.Length / filehash.Length; i++)
            {
                byte[] buffer = new byte[filehash.Length];
                blockhashstream.Read(buffer, i * filehash.Length, filehash.Length);
                blockhashes.Add(buffer);
            }
            blockhashstream.Close();

            MemoryStream file = new MemoryStream();
            foreach (var hash in blockhashes)
            {
                BlobLocation blockloc = GetBackupLocation(hash);
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

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bptdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // keysize = BitConverter.GetBytes(int) (only used for decoding HashBLocationPairs)
            // HashBLocationPairs = enum_encode(List<byte[]> [hash,... & backuplocation.serialize(),...])

            bptdata.Add("keysize-v1", BitConverter.GetBytes(20));

            List<byte[]> binkeyvals = new List<byte[]>();
            foreach (KeyValuePair<byte[], BlobLocation> kvp in TreeIndexStore)
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
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            int keysize = BitConverter.ToInt32(savedobjects["keysize-v1"], 0);

            foreach (byte[] binkvp in BinaryEncoding.enum_decode(savedobjects["HashBLocationPairs-v1"]))
            {
                byte[] keybytes = new byte[keysize];
                byte[] backuplocationbytes = new byte[binkvp.Length - keysize];
                Array.Copy(binkvp, keybytes, keysize);
                Array.Copy(binkvp, keysize, backuplocationbytes, 0, binkvp.Length - keysize);

                this.AddHash(keybytes, BlobLocation.deserialize(backuplocationbytes));
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
