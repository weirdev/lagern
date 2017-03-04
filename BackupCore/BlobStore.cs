using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

        /// <summary>
        /// Add list of blocks to blobstore. Automatically creates a reference blob (hashlist) if blocks.count > 1
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blocks"></param>
        /// <param name="type"></param>
        public void AddBlob(byte[] hash, List<HashBlockPair> blocks, BlobLocation.BlobTypes type)
        {
            if (blocks.Count == 1)
            {
                AddBlob(blocks[0], type);
            }
            else
            {
                byte[] hashlist = new byte[blocks[0].Hash.Length * blocks.Count];
                for (int i = 0; i < blocks.Count; i++)
                {
                    AddBlob(blocks[i], BlobLocation.BlobTypes.Simple, false);
                    Array.Copy(blocks[i].Hash, 0, hashlist, blocks[0].Hash.Length * i, blocks[0].Hash.Length);
                }
                AddMultiBlockReferenceBlob(hash, hashlist, type);
            }
        }

        /// <summary>
        /// Add a single blob to blobstore.
        /// </summary>
        /// <param name="block"></param>
        /// <param name="type"></param>
        /// <param name="isMultiBlockReference"></param>
        public void AddBlob(HashBlockPair block, BlobLocation.BlobTypes type)
        {
            AddBlob(block, type, false);
        }

        private void AddBlob(HashBlockPair block, BlobLocation.BlobTypes type, bool isMultiBlockReference)
        {
            string relpath = HashTools.ByteArrayToHexViaLookup32(block.Hash);
            BlobLocation posblocation = new BlobLocation(type, isMultiBlockReference, relpath, 0, block.Block.Length);
            bool alreadystored = false;
            lock (this)
            {
                // Have we already stored this 
                alreadystored = AddHash(block.Hash, posblocation);
            }
            if (!alreadystored)
            {
                WriteBlob(posblocation, block.Block);
            }
        }

        public void AddMultiBlockReferenceBlob(byte[] hash, byte[] hashlist, BlobLocation.BlobTypes type)
        {
            HashBlockPair referenceblock = new HashBlockPair(hash, hashlist);
            AddBlob(referenceblock, type, true);
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
            return TreeIndexStore.GetRecord(hash) != null;
        }

        public BlobLocation GetBlobLocation(byte[] hash)
        {
            return TreeIndexStore.GetRecord(hash);
        }

        public byte[] GetBlob(byte[] filehash)
        {
            BlobLocation blobbl = GetBlobLocation(filehash);
            if (blobbl.IsMultiBlockReference) // File is comprised of multiple blocks
            {
                var blockhashes = GetHashListFromBlob(blobbl);

                MemoryStream file = new MemoryStream();
                foreach (var hash in blockhashes)
                {
                    BlobLocation blockloc = GetBlobLocation(hash);
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
            else // file is single block
            {
                FileStream blockstream = File.OpenRead(Path.Combine(BlockSaveDirectory, blobbl.RelativeFilePath));
                byte[] buffer = new byte[blockstream.Length];
                blockstream.Read(buffer, 0, blobbl.ByteLength);
                blockstream.Close();
                return buffer;
            }
        }

        private List<byte[]> GetHashListFromBlob(BlobLocation blocation)
        {
            if (!blocation.IsMultiBlockReference)
            {
                throw new ArgumentException("blobhash must be of a blob with IsMultiBlockReference=true");
            }
            FileStream blockhashstream = File.OpenRead(Path.Combine(BlockSaveDirectory, blocation.RelativeFilePath));
            List<byte[]> blockhashes = new List<byte[]>();
            for (int i = 0; i < blockhashstream.Length / 20; i++)
            {
                byte[] buffer = new byte[20];
                blockhashstream.Read(buffer, i * 20, 20);
                blockhashes.Add(buffer);
            }
            blockhashstream.Close();
            return blockhashes;
        }

        /// <summary>
        /// Wraps other storedata method using input stream. Creates MemoryStream from inputdata.
        /// </summary>
        /// <param name="inputdata"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblockqueue"></param>
        protected void SplitData(byte[] inputdata, BlobLocation.BlobTypes type, byte[] filehash, BlockingCollection<HashBlockPair> hashblockqueue)
        {
            SplitData(new MemoryStream(inputdata), type, filehash, hashblockqueue);
        }

        /// <summary>
        /// Chunks and saves data to blobstore. Operates on stream input, so Filestreams can be used and entire files need not be loaded into memory.
        /// </summary>
        /// <param name="inputstream"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblockqueue"></param>
        protected void SplitData(Stream inputstream, BlobLocation.BlobTypes type, byte[] filehash, BlockingCollection<HashBlockPair> hashblockqueue)
        {
            // https://rsync.samba.org/tech_report/node3.html
            List<byte> newblock = new List<byte>();
            byte[] alphachksum = new byte[16];
            byte[] betachksum = new byte[16];
            SHA1 sha1filehasher = SHA1.Create();
            SHA1 sha1blockhasher = SHA1.Create();


            int readsize = 8388608;
            int rollwindowsize = 32;
            try
            {
                for (int i = 0; i < inputstream.Length; i += readsize) // read the file in larger chunks for efficiency
                {
                    byte[] readin;
                    if (i + readsize <= inputstream.Length) // readsize or more bytes left to read
                    {
                        readin = new byte[readsize];
                        inputstream.Read(readin, 0, readsize);
                    }
                    else // < readsize bytes left to read
                    {
                        readin = new byte[inputstream.Length % readsize];
                        inputstream.Read(readin, 0, (int)(inputstream.Length % readsize));
                    }
                    for (int j = 0; j < readin.Length; j++) // Byte by byte
                    {
                        newblock.Add(readin[j]);
                        HashTools.ByteSum(alphachksum, newblock[newblock.Count - 1]);
                        if (newblock.Count > rollwindowsize)
                        {
                            HashTools.ByteDifference(alphachksum, newblock[newblock.Count - rollwindowsize - 1]);
                            byte[] shifted = new byte[16];
                            shifted[0] = (byte)((newblock[newblock.Count - 1] << 5) & 0xFF); // rollwindowsize = 32 = 2^5 => 5
                            shifted[1] = (byte)((newblock[newblock.Count - 1] >> 3) & 0xFF); // 8-5 = 3
                            HashTools.BytesDifference(betachksum, shifted);
                        }
                        HashTools.BytesSum(betachksum, alphachksum);


                        if (alphachksum[15] == 0xFF && betachksum[0] == 0xFF && betachksum[15] == 0xFE) // (256*256*128)^-1 => expected value (/2) = ~4MB
                        {
                            byte[] block = newblock.ToArray();
                            if (i >= inputstream.Length && j >= readin.Length) // Need to use TransformFinalBlock if at end of input
                            {
                                sha1filehasher.TransformFinalBlock(block, 0, block.Length);
                            }
                            else
                            {
                                sha1filehasher.TransformBlock(block, 0, block.Length, block, 0);
                            }
                            hashblockqueue.Add(new HashBlockPair(sha1blockhasher.ComputeHash(block), block));
                            newblock = new List<byte>();
                        }
                    }
                }
                if (newblock.Count != 0) // Create block from remaining bytes
                {
                    byte[] block = newblock.ToArray();
                    sha1filehasher.TransformFinalBlock(block, 0, block.Length);
                    hashblockqueue.Add(new HashBlockPair(sha1blockhasher.ComputeHash(block), block));
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Error");
            }
            finally
            {
                inputstream.Close();
            }
            Array.Copy(sha1filehasher.Hash, filehash, sha1filehasher.Hash.Length);
            hashblockqueue.CompleteAdding();
        }

        public byte[] StoreDataAsync(byte[] inputdata, BlobLocation.BlobTypes type)
        {
            return StoreDataAsync(new MemoryStream(inputdata), type);
        }

        public byte[] StoreDataAsync(Stream readerbuffer, BlobLocation.BlobTypes type)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            Task getfileblockstask = Task.Run(() => SplitData(readerbuffer, type, filehash, fileblockqueue));

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    this.AddBlob(block, BlobLocation.BlobTypes.Simple);
                    blockshashes.Add(block.Hash);
                }
            }
            if (blockshashes.Count > 1)
            {
                // Multiple blocks so create hashlist blob to reference them all together
                byte[] hashlist = new byte[blockshashes.Count * blockshashes[0].Length];
                for (int i = 0; i < blockshashes.Count; i++)
                {
                    Array.Copy(blockshashes[i], 0, hashlist, i * blockshashes[i].Length, blockshashes[i].Length);
                }
                AddMultiBlockReferenceBlob(filehash, hashlist, type);
            }
            else
            {
                // Just the one block, so change its type to `type`
                GetBlobLocation(filehash).BlobType = type; // filehash should match individual block hash used earlier since total file == single block
            }
            return filehash;
        }

        public byte[] StoreDataSync(byte[] inputdata, BlobLocation.BlobTypes type)
        {
            return StoreDataSync(new MemoryStream(inputdata), type);
        }

        /// <summary>
        /// Backup data sychronously.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        public byte[] StoreDataSync(Stream readerbuffer, BlobLocation.BlobTypes type)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            SplitData(readerbuffer, type, filehash, fileblockqueue);

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    this.AddBlob(block, BlobLocation.BlobTypes.Simple);
                    blockshashes.Add(block.Hash);
                }
            }
            if (blockshashes.Count > 1)
            {
                // Multiple blocks so create hashlist blob to reference them all together
                byte[] hashlist = new byte[blockshashes.Count * blockshashes[0].Length];
                for (int i = 0; i < blockshashes.Count; i++)
                {
                    Array.Copy(blockshashes[i], 0, hashlist, i * blockshashes[i].Length, blockshashes[i].Length);
                }
                AddMultiBlockReferenceBlob(filehash, hashlist, type);
            }
            else
            {
                // Just the one block, so change its type to FileBlob
                GetBlobLocation(filehash).BlobType = type; // filehash should match individual block hash used earlier since total file == single block
            }
            return filehash;
        }

        public void DereferenceOneDegree(byte[] blobhash)
        {
            BlobLocation blocation = GetBlobLocation(blobhash);
            blocation.ReferenceCount -= 1;
            if (blocation.IsMultiBlockReference)
            {
                foreach (var hash in GetHashListFromBlob(blocation))
                {
                    DereferenceOneDegree(hash);
                }
            }
            switch (blocation.BlobType)
            {
                case BlobLocation.BlobTypes.Simple:
                    break;
                case BlobLocation.BlobTypes.FileBlob:
                    break;
                case BlobLocation.BlobTypes.MetadataTree:
                    MetadataTree mtree = MetadataTree.deserialize(GetBlob(blobhash));
                    foreach (var filehash in mtree.GetAllFileHashes())
                    {
                        DereferenceOneDegree(filehash);
                    }
                    break;
                default:
                    break;
            }
            if (blocation.ReferenceCount <= 0)
            {
                File.Delete(Path.Combine(BlockSaveDirectory, blocation.RelativeFilePath));
                TreeIndexStore.RemoveKey(blobhash);
            }
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
