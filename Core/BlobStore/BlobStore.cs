using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackupCore
{
    /// <summary>
    /// Binary tree holding hashes and their corresponding locations in backup
    /// </summary>
    public class BlobStore
    {
        private BPlusTree<BlobLocation> IndexStore { get; set; }

        public IBlobStoreDependencies Dependencies { get; set; }

        public BlobStore(IBlobStoreDependencies dependencies)
        {
            IndexStore = new BPlusTree<BlobLocation>(100);
            Dependencies = dependencies;
        }

        public byte[] StoreData(string backupset, byte[] inputdata)
        {
            return StoreData(backupset, new MemoryStream(inputdata));
        }

        /// <summary>
        /// Backup data sychronously.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        public byte[] StoreData(string backupset, Stream readerbuffer)
        {
            BlockingCollection<HashBlobPair> fileblobqueue = new BlockingCollection<HashBlobPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            SplitData(readerbuffer, filehash, fileblobqueue);

            List<byte[]> blobshashes = new List<byte[]>();
            while (!fileblobqueue.IsCompleted)
            {
                if (fileblobqueue.TryTake(out HashBlobPair blob))
                {
                    AddBlob(backupset, blob);
                    blobshashes.Add(blob.Hash);
                }
            }
            if (blobshashes.Count > 1)
            {
                // Multiple blobs so create hashlist reference to reference them all together
                AddMultiBlobReferenceBlob(backupset, filehash, blobshashes);
            }
            return filehash;
        }

        public byte[] RetrieveData(byte[] filehash)
        {
            BlobLocation blobbl = GetBlobLocation(filehash);
            if (blobbl.BlockHashes != null) // File is comprised of multiple blobs
            {
                MemoryStream outstream = new MemoryStream();
                foreach (var hash in blobbl.BlockHashes)
                {
                    BlobLocation blobloc = GetBlobLocation(hash);
                    outstream.Write(LoadBlob(blobloc, hash), 0, blobloc.ByteLength);
                }
                byte[] filedata = outstream.ToArray();
                outstream.Close();
                return filedata;
            }
            else // file is single blob
            {
                return LoadBlob(blobbl, filehash);
            }
        }
        
        public void CacheBlobList(string backupsetname, BlobStore cacheblobs)
        {
            string bloblistcachebsname = backupsetname + Core.BlobListCacheSuffix;
            cacheblobs.RemoveAllBackupSetReferences(bloblistcachebsname);
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in GetAllHashesAndBlobLocations(backupsetname))
            {
                cacheblobs.AddBlob(bloblistcachebsname, new HashBlobPair(hashblob.Key, null), hashblob.Value.BlockHashes, true);
            }
        }

        /// <summary>
        /// Loads the data from a blob, no special handling of multiblob references etc.
        /// </summary>
        /// <param name="blocation"></param>
        /// <param name="hash">Null for no verification</param>
        /// <returns></returns>
        private byte[] LoadBlob(BlobLocation blocation, byte[] hash, int retries=1)
        {
            byte[] data = Dependencies.LoadBlob(blocation.EncryptedHash);
            if (HashTools.GetSHA1Hasher().ComputeHash(data).SequenceEqual(hash))
            {
                return data;
            }
            else if (retries > 0)
            {
                return LoadBlob(blocation, hash, retries - 1);
            }
            throw new Exception("Blob data did not match hash.");
        }

        public void DecrementReferenceCount(string backupsetname, byte[] blobhash, BlobLocation.BlobType blobtype,
            bool includefiles)
        {
            BlobLocation rootBlobLocation = GetBlobLocation(blobhash);
            if (rootBlobLocation.TotalReferenceCount == 1) // To be deleted?
            {
                IBlobReferenceIterator blobReferences = GetAllBlobReferences(blobhash, blobtype, includefiles, false);
                foreach (var reference in blobReferences)
                {
                    BlobLocation blocation = GetBlobLocation(reference);

                    // When we finish iterating over the children, decrement this blob
                    blobReferences.PostOrderAction(() => IncrementReferenceCountNoRecurse(backupsetname, blocation, reference, -1));
                    if (!(rootBlobLocation.TotalReferenceCount == 1)) // Not to be deleted?
                    {
                        // Dont need to decrement child references if this wont be deleted
                        blobReferences.SkipChildren();
                    }
                }
            }
            IncrementReferenceCountNoRecurse(backupsetname, rootBlobLocation, blobhash, -1); // must delete parent last so parent can be loaded/used in GetAllBlobReferences()
        }

        private void IncrementReferenceCountNoRecurse(string backupset, byte[] blobhash, int amount) => 
            IncrementReferenceCountNoRecurse(backupset, GetBlobLocation(blobhash), blobhash, amount);

        private void IncrementReferenceCountNoRecurse(string backupset, BlobLocation blocation, byte[] blobhash, int amount)
        {
            bool originallyshallow = blocation.TotalNonShallowReferenceCount == 0;
            if (blocation.BSetReferenceCounts.ContainsKey(backupset))
            {
                blocation.BSetReferenceCounts[backupset] += amount;
            }
            else
            {
                blocation.BSetReferenceCounts[backupset] = amount;
            }
            if (blocation.BSetReferenceCounts[backupset] == 0)
            {
                blocation.BSetReferenceCounts.Remove(backupset);
            }
            else if (blocation.BSetReferenceCounts[backupset] < 0)
            {
                throw new Exception("Negative reference count in blobstore");
            }
            if (blocation.BlockHashes == null) // Can't delete from disk if this is a multiblock reference (does not directly store data on disk)
            {
                if (blocation.TotalNonShallowReferenceCount == 0)
                {
                    if (!originallyshallow)
                    {
                        try
                        {
                            Dependencies.DeleteBlob(blocation.EncryptedHash, blocation.RelativeFilePath);
                        }
                        catch (Exception)
                        {
                            throw new Exception("Error deleting unreferenced file.");
                        }
                    }
                }
            }
            if (blocation.TotalReferenceCount == 0)
            {
                IndexStore.Remove(blobhash);
            }
        }

        // TODO: Update transfer logic with new reference counting logic
        public void TransferBackup(BlobStore dst, string dstbackupset, byte[] bblobhash, bool includefiles)
        {
            TransferBlobAndReferences(dst, dstbackupset, bblobhash, BlobLocation.BlobType.BackupRecord, includefiles);
        }

        private void TransferBlobAndReferences(BlobStore dst, string dstbackupset, byte[] blobhash, 
            BlobLocation.BlobType blobtype, bool includefiles)
        {
            BlobLocation rootDstBlobLocation;
            try
            {
                rootDstBlobLocation = dst.GetBlobLocation(blobhash);
            }
            catch (KeyNotFoundException)
            {
                rootDstBlobLocation = TransferBlobNoReferences(dst, dstbackupset, blobhash, GetBlobLocation(blobhash), includefiles);

                IBlobReferenceIterator blobReferences = dst.GetAllBlobReferences(blobhash, blobtype, includefiles, false);
                foreach (var reference in blobReferences)
                {
                    BlobLocation dstBlobLocation;
                    try
                    {
                        dstBlobLocation = dst.GetBlobLocation(reference);
                        // Dont need to increment child references if this already exists
                        blobReferences.SkipChildren();
                    }               
                    catch (KeyNotFoundException)
                    {
                        dstBlobLocation = TransferBlobNoReferences(dst, dstbackupset, reference, GetBlobLocation(reference), includefiles);
                    }
                    // When we finish iterating over the children, increment this blob
                    blobReferences.PostOrderAction(() => dst.IncrementReferenceCountNoRecurse(dstbackupset, dstBlobLocation, reference, 1));
                }
            }
            dst.IncrementReferenceCountNoRecurse(dstbackupset, rootDstBlobLocation, blobhash, 1);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="blobhash"></param>
        /// <returns>True Blob exists in destination</returns>
        private BlobLocation TransferBlobNoReferences(BlobStore dst, string dstbackupset, 
            byte[] blobhash, BlobLocation blocation, bool includefiles)
        {
            if (blocation.BlockHashes == null)
            {
                return dst.AddBlob(dstbackupset, new HashBlobPair(blobhash, LoadBlob(blocation, blobhash)));
            }
            else
            {
                return dst.AddMultiBlobReferenceBlob(dstbackupset, blobhash, blocation.BlockHashes);
            }

        }

        /// <summary>
        /// Adds a hash and corresponding BackupLocation to the Index.
        /// Does not modify reference counts.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns>
        /// Returns the existing location if we already have the hash stored. Null if we used
        /// the parameter blocation and need to save the corresponding blob data.
        /// </returns>
        private (BlobLocation existinglocation, bool datastored)? AddHash(byte[] hash, BlobLocation blocation)
        {
            // Adds a hash and Blob Location to the BlockHashStore
            BlobLocation existingblocation = IndexStore.AddOrFind(hash, blocation);
            if (existingblocation == null)
            {
                return null;
            }
            return (existingblocation, existingblocation.TotalNonShallowReferenceCount > 0);
        }

        /// <summary>
        /// Add a single blob to blobstore.
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="type"></param>
        /// <returns>The BlobLocation the blob is saved to.</returns>
        private BlobLocation AddBlob(string backupset, HashBlobPair blob)
        {
            return AddBlob(backupset, blob, null);
        }

        private BlobLocation AddBlob(string backupset, HashBlobPair blob, List<byte[]> blockreferences, bool shallow=false)
        {
            // We navigate down 

            // Where we will put the blob data if we dont already have it stored
            BlobLocation posblocation;
            if (shallow)
            {
                posblocation = new BlobLocation(blockreferences);
            }
            else
            {
                if (blockreferences == null)
                {
                    posblocation = new BlobLocation(null, "", blob.Block.Length);
                }
                else
                {
                    posblocation = new BlobLocation(blockreferences);
                }
            }             

            // Where the data is already stored if it exists
            (BlobLocation bloc, bool datastored)? existingblocstored;
            lock (this)
            {
                // Have we already stored this?
                existingblocstored = AddHash(blob.Hash, posblocation);
            }
            if (existingblocstored == null) // ExistBloc == null means posbloc was just added
            {
                if (!shallow)
                {
                    if (blockreferences == null)
                    {
                        (posblocation.EncryptedHash, posblocation.RelativeFilePath) = WriteBlob(blob.Hash, blob.Block);
                    }
                }
                else
                {
                    posblocation.RelativeFilePath = "";
                    posblocation.EncryptedHash = blob.Hash;
                }
                // Dont change reference counts until finalization
                // IncrementReferenceCountNoRecurse(backupset, posblocation, blob.Hash, 1);
                return posblocation;
            }
            else // Existbloc already stored at dst
            {
                (BlobLocation existingbloc, bool datastored) = existingblocstored.Value;
                // Is the data not already stored in the blobstore (are all references shallow thus far)?
                if (existingbloc.BlockHashes == null)
                {
                    if (!datastored)
                    {
                        // Data is not already stored
                        // Dont save if we are writing a bloblistcache
                        if (!backupset.EndsWith(Core.BlobListCacheSuffix))
                        {
                            // If we are saving to a cache and the bloblist cache indicates the destination has the data
                            // Then dont store, Else save
                            if (!(backupset.EndsWith(Core.CacheSuffix)
                                && existingbloc.BSetReferenceCounts.ContainsKey(backupset.Substring(0,
                                    backupset.Length - Core.CacheSuffix.Length) + Core.BlobListCacheSuffix)))
                            {
                                (existingbloc.EncryptedHash, existingbloc.RelativeFilePath) = WriteBlob(blob.Hash, blob.Block);
                            }
                        }
                    }
                }
                // Dont change reference counts until finalization
                // IncrementReferenceCountNoRecurse(backupset, existingbloc, blob.Hash, 1);
                return existingbloc;
            }
        }

        public void FinalizeBlobAddition(string bsname, byte[] blobhash, BlobLocation.BlobType blobType)
        {
            // Handle root blob
            BlobLocation rootblocation = GetBlobLocation(blobhash);
            if (rootblocation.TotalReferenceCount == 0)
            {
                IBlobReferenceIterator blobReferences = GetAllBlobReferences(blobhash, blobType, true, false);
                // Loop through children
                foreach (byte[] reference in blobReferences)
                {
                    BlobLocation blocation = GetBlobLocation(blobhash);
                    if (blocation.TotalReferenceCount > 0) // This was already stored
                    {
                        blobReferences.SkipChildren();
                    }
                    IncrementReferenceCountNoRecurse(bsname, blocation, blobhash, 1);
                }
            }
            // Increment root blob
            IncrementReferenceCountNoRecurse(bsname, rootblocation, blobhash, 1);
        }

        // TODO: should we just have to iteratively remove each backup in the bset?
        public void RemoveAllBackupSetReferences(string bsname)
        {
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in IndexStore)
            {
                if (hashblob.Value.BSetReferenceCounts.ContainsKey(bsname))
                {
                    IncrementReferenceCountNoRecurse(bsname, hashblob.Key, -hashblob.Value.BSetReferenceCounts[bsname]);
                }
            }
        }

        private BlobLocation AddMultiBlobReferenceBlob(string backupset, byte[] hash, List<byte[]> hashlist)
        {
            HashBlobPair referenceblob = new HashBlobPair(hash, null);
            return AddBlob(backupset, referenceblob, hashlist);
        }

        public IBlobReferenceIterator GetAllBlobReferences(byte[] blobhash, BlobLocation.BlobType blobtype,
            bool includefiles, bool bottomup=true)
        {
            return new BlobReferenceIterator(this, blobhash, blobtype, includefiles, bottomup);
        }

        private (byte[] encryptedHash, string fileId) WriteBlob(byte[] hash, byte[] blob)
        {
            return Dependencies.StoreBlob(hash, blob);
        }

        public bool ContainsHash(byte[] hash)
        {
            return IndexStore.GetRecord(hash) != null;
        }

        public bool ContainsHash(string backupset, byte[] hash)
        {
            BlobLocation blocation = IndexStore.GetRecord(hash);
            if (blocation != null)
            {
                return blocation.BSetReferenceCounts.ContainsKey(backupset);
            }
            return false;
        }

        public BlobLocation GetBlobLocation(byte[] hash)
        {
            BlobLocation blocation = IndexStore.GetRecord(hash);
            if (blocation == null)
            {
                throw new KeyNotFoundException("The given hash does not exist in the Blob Index.");
            }
            return blocation;
        }

        /// <summary>
        /// Wraps other storedata method for byte arrays. Creates MemoryStream from inputdata.
        /// </summary>
        /// <param name="inputdata"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblobqueue"></param>
        public void SplitData(byte[] inputdata, byte[] filehash, BlockingCollection<HashBlobPair> hashblobqueue)
        {
            SplitData(new MemoryStream(inputdata), filehash, hashblobqueue);
        }

        /// <summary>
        /// Chunks and saves data to blobstore. 
        /// Operates on stream input, so Filestreams can be used and 
        /// entire files need not be loaded into memory.
        /// If an error occurs (typically when reading from a stream
        /// representing a file), it is thrown to the caller.
        /// </summary>
        /// <param name="inputstream"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblobqueue"></param>
        public void SplitData(Stream inputstream, byte[] filehash, BlockingCollection<HashBlobPair> hashblobqueue)
        {
            // https://rsync.samba.org/tech_report/node3.html
            List<byte> newblob = new List<byte>();
            byte[] alphachksum = new byte[2];
            byte[] betachksum = new byte[2];
            SHA1 sha1filehasher = HashTools.GetSHA1Hasher();
            SHA1 sha1blobhasher = HashTools.GetSHA1Hasher(); ;

            if (inputstream.Length != 0)
            {
                int readsize = 8_388_608;
                int rollwindowsize = 32;
                byte[] readin;
                byte[] shifted = new byte[2];
                for (int i = 0; i < inputstream.Length; i += readsize) // read the file in larger chunks for efficiency
                {
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
                        newblob.Add(readin[j]);
                        HashTools.ByteSum(alphachksum, newblob[newblob.Count - 1]);
                        if (newblob.Count > rollwindowsize)
                        {
                            HashTools.ByteDifference(alphachksum, newblob[newblob.Count - rollwindowsize - 1]);
                            shifted[0] = (byte)((newblob[newblob.Count - 1] << 5) & 0xFF); // rollwindowsize = 32 = 2^5 => 5
                            shifted[1] = (byte)((newblob[newblob.Count - 1] >> 3) & 0xFF); // 8-5 = 3
                            HashTools.BytesDifference(betachksum, shifted);
                        }
                        HashTools.BytesSum(betachksum, alphachksum);

                        if (alphachksum[0] == 0xFF && betachksum[0] == 0xFF && betachksum[1] < 0x02) // (256*256*128)^-1 => expected value (/2) = ~4MB
                        {
                            byte[] blob = newblob.ToArray();
                            if (i + readsize >= inputstream.Length && j + 1 >= readin.Length) // Need to use TransformFinalBlock if at end of input
                            {
                                sha1filehasher.TransformFinalBlock(blob, 0, blob.Length);
                            }
                            else
                            {
                                sha1filehasher.TransformBlock(blob, 0, blob.Length, blob, 0);
                            }
                            hashblobqueue.Add(new HashBlobPair(sha1blobhasher.ComputeHash(blob), blob));
                            newblob = new List<byte>();
                            Array.Clear(alphachksum, 0, 2);
                            Array.Clear(betachksum, 0, 2);
                        }
                    }
                }
                if (newblob.Count != 0) // Create blob from remaining bytes
                {
                    byte[] blob = newblob.ToArray();
                    sha1filehasher.TransformFinalBlock(blob, 0, blob.Length);
                    hashblobqueue.Add(new HashBlobPair(sha1blobhasher.ComputeHash(blob), blob));
                }
            }
            else
            {
                byte[] blob = new byte[0];
                sha1filehasher.TransformFinalBlock(blob, 0, blob.Length);
                hashblobqueue.Add(new HashBlobPair(sha1blobhasher.ComputeHash(blob), blob));
            }
            Array.Copy(sha1filehasher.Hash, filehash, sha1filehasher.Hash.Length);
            hashblobqueue.CompleteAdding();
        }

        /// <summary>
        /// Calculates the size of the blobs and child blobs referenced by the given hash.
        /// </summary>
        /// <param name="blobhash"></param>
        /// <returns>(Size of all referenced blobs, size of blobs referenced only by the given hash and its children)</returns>
        public (int allreferences, int uniquereferences) GetSizes(byte[] blobhash, BlobLocation.BlobType blobtype)
        {
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize = new Dictionary<string, (int, BlobLocation)>();
            GetBlobReferenceFrequencies(blobhash, blobtype, hashfreqsize);
            int allreferences = 0;
            int uniquereferences = 0;
            foreach (var reference in hashfreqsize.Values)
            {
                allreferences += reference.blocation.ByteLength * reference.frequency;
                if (reference.blocation.TotalReferenceCount == reference.frequency)
                {
                    uniquereferences += reference.blocation.ByteLength; // TODO: unique referenes 
                }
            }
            return (allreferences, uniquereferences);
        }

        private void GetBlobReferenceFrequencies(byte[] blobhash, BlobLocation.BlobType blobtype, 
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize) 
        {
            GetReferenceFrequenciesNoRecurse(blobhash, hashfreqsize);
            foreach (var reference in GetAllBlobReferences(blobhash, blobtype, true))
            {
                GetReferenceFrequenciesNoRecurse(reference, hashfreqsize);
            }
        }

        private void GetReferenceFrequenciesNoRecurse(byte[] blobhash, Dictionary<string, 
            (int frequency, BlobLocation blocation)> hashfreqsize)
        {
            string hashstring = HashTools.ByteArrayToHexViaLookup32(blobhash);
            BlobLocation blocation = GetBlobLocation(blobhash);
            if (hashfreqsize.ContainsKey(hashstring))
            {
                hashfreqsize[hashstring] = (hashfreqsize[hashstring].frequency + 1, hashfreqsize[hashstring].blocation);
            }
            else
            {
                hashfreqsize.Add(hashstring, (1, blocation));
            }
        }

        private IEnumerable<KeyValuePair<byte[], BlobLocation>> GetAllHashesAndBlobLocations(string bsname)
        {
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in IndexStore)
            {
                if (hashblob.Value.BSetReferenceCounts.ContainsKey(bsname))
                {
                    yield return hashblob;
                }
            }
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bptdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // keysize = BitConverter.GetBytes(int) (only used for decoding HashBLocationPairs)
            // HashBLocationPairs = enum_encode(List<byte[]> [hash,... & backuplocation.serialize(),...])
            // -"-v2"
            // IsCache = BitConverter.GetBytes(bool)
            // -"-v3"
            // Removed IsCache

            bptdata.Add("keysize-v1", BitConverter.GetBytes(20));

            List<byte[]> binkeyvals = new List<byte[]>();
            foreach (KeyValuePair<byte[], BlobLocation> kvp in IndexStore)
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

        public static BlobStore deserialize(byte[] data, IBlobStoreDependencies dependencies)
        {

            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            int keysize = BitConverter.ToInt32(savedobjects["keysize-v1"], 0);

            BlobStore bs = new BlobStore(dependencies);
            bs.IndexStore = new BPlusTree<BlobLocation>(DeconstructHashBlocationPairs(savedobjects["HashBLocationPairs-v1"], keysize), 
                                bs.IndexStore.NodeSize);
            return bs;
        }

        private static IEnumerable<KeyValuePair<byte[], BlobLocation>> DeconstructHashBlocationPairs(byte[] hblp, int keysize)
        {
            foreach (byte[] binkvp in BinaryEncoding.enum_decode(hblp))
            {
                byte[] keybytes = new byte[keysize];
                byte[] backuplocationbytes = new byte[binkvp.Length - keysize];
                Array.Copy(binkvp, keybytes, keysize);
                Array.Copy(binkvp, keysize, backuplocationbytes, 0, binkvp.Length - keysize);

                yield return new KeyValuePair<byte[], BlobLocation>(keybytes, BlobLocation.deserialize(backuplocationbytes));
            }
        }

        private class BlobReferenceIterator : IBlobReferenceIterator
        {
            public byte[] ParentHash { get; set; }

            public BlobStore Blobs { get; set; }

            private bool IncludeFiles { get; set; }

            public bool BottomUp { get; set; } // Determines whether or not to return child references first
            
            private bool skipchild = false;
            private IBlobReferenceIterator childiterator = null;

            private BlobLocation.BlobType BlobType { get; set; }

            private Action postOrderAction = null;
            
            public BlobReferenceIterator(BlobStore blobs, byte[] blobhash, BlobLocation.BlobType blobtype, 
                bool includefiles, bool bottomup)
            {
                Blobs = blobs;
                ParentHash = blobhash;
                BottomUp = bottomup;
                IncludeFiles = includefiles;
                BlobType = blobtype;
            }

            public void SkipChildren()
            {
                if (childiterator != null)
                {
                    childiterator.SkipChildren();
                }
                else
                {
                    skipchild = true;
                }
                if (BottomUp)
                {
                    throw new Exception("Skip child is not valid when iterating through references bottum up.");
                }
            }

            public void PostOrderAction(Action action)
            {
                if (childiterator != null)
                {
                    childiterator.PostOrderAction(action);
                }
                else
                {
                    postOrderAction = action;
                }
                if (BottomUp)
                {
                    throw new Exception("Post order action is not valid when iterating through references bottum up.");
                }
            }

            public IEnumerator<byte[]> GetEnumerator()
            {
                // NOTE: This recursive iteration creates many iterators as it runs
                // this may cause performance issues.

                // NOTE: Since the blobs references returned may be being operated on (blobs deleted)
                // and this method relies on being able to load the input (parent) blob
                // we return the parent blobs last in all recursions
                BlobLocation blocation = Blobs.GetBlobLocation(ParentHash);
                switch (BlobType)
                {
                    case BlobLocation.BlobType.Simple:
                        break;
                    case BlobLocation.BlobType.FileBlob:
                        break;
                    case BlobLocation.BlobType.BackupRecord:
                        BackupRecord br = BackupRecord.deserialize(Blobs.RetrieveData(ParentHash));
                        if (!BottomUp)
                        {
                            yield return br.MetadataTreeHash; // return 1 immediate reference
                        }
                        if (!skipchild)
                        {
                            childiterator = new BlobReferenceIterator(Blobs, br.MetadataTreeHash, 
                                BlobLocation.BlobType.MetadataNode, IncludeFiles, BottomUp);
                            foreach (var refref in childiterator) // recurse on references of that reference
                            {
                                yield return refref;
                            }
                            childiterator = null;
                        }
                        skipchild = false;
                        if (BottomUp)
                        {
                            yield return br.MetadataTreeHash; // return 1 immediate reference
                        }
                        break;
                    case BlobLocation.BlobType.MetadataNode:
                        IEnumerable<byte[]> dirreferences;
                        IEnumerable<byte[]> filereferences = null;
                        byte[] mnodebytes = Blobs.RetrieveData(ParentHash);
                        dirreferences = MetadataNode.GetImmediateChildNodeReferencesWithoutLoad(mnodebytes); // many immediate references
                        if (IncludeFiles)
                        {
                            filereferences = MetadataNode.GetImmediateFileReferencesWithoutLoad(mnodebytes);
                            foreach (var fref in filereferences)
                            {
                                if (!BottomUp)
                                {
                                    yield return fref;
                                }
                                if (!skipchild)
                                {
                                    childiterator = new BlobReferenceIterator(Blobs, fref, BlobLocation.BlobType.FileBlob, 
                                        IncludeFiles, BottomUp);
                                    foreach (var frefref in childiterator)
                                    {
                                        yield return frefref;
                                    }
                                    childiterator = null;
                                }
                                skipchild = false;
                                if (BottomUp)
                                {
                                    yield return fref;
                                }
                            }
                        }
                        foreach (var reference in dirreferences) // for each immediate reference
                        {
                            if (!BottomUp)
                            {
                                yield return reference; // return immediate reference
                            }
                            if (!skipchild)
                            {
                                childiterator = new BlobReferenceIterator(Blobs, reference, BlobLocation.BlobType.MetadataNode, 
                                    IncludeFiles, BottomUp);
                                foreach (var refref in childiterator) // recurse on references of that reference
                                {
                                    yield return refref;
                                }
                            }
                            skipchild = false;
                            childiterator = null;
                            if (BottomUp)
                            {
                                yield return reference; // return immediate reference
                            }
                        }
                        break;
                    default:
                        throw new Exception("Unhandled blobtype on dereference");
                }
                if (blocation.BlockHashes != null)
                {
                    foreach (var hash in blocation.BlockHashes)
                    {
                        yield return hash;
                    }
                }

                // Run post order action if exists
                postOrderAction?.Invoke();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        /// <summary>
        /// Skip iterating over any child references of current blob
        /// </summary>
        public interface IBlobReferenceIterator : IEnumerable<byte[]>
        {
            void SkipChildren();
            void PostOrderAction(Action action);
        }
    }
}
