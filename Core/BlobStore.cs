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

        public byte[] StoreData(string backupset, byte[] inputdata, BlobLocation.BlobTypes type)
        {
            return StoreData(backupset, new MemoryStream(inputdata), type);
        }

        /// <summary>
        /// Backup data sychronously.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        public byte[] StoreData(string backupset, Stream readerbuffer, BlobLocation.BlobTypes type)
        {
            BlockingCollection<HashBlobPair> fileblobqueue = new BlockingCollection<HashBlobPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            SplitData(readerbuffer, filehash, fileblobqueue);

            List<byte[]> blobshashes = new List<byte[]>();
            while (!fileblobqueue.IsCompleted)
            {
                if (fileblobqueue.TryTake(out HashBlobPair blob))
                {
                    AddBlob(backupset, blob, BlobLocation.BlobTypes.Simple);
                    blobshashes.Add(blob.Hash);
                }
            }
            if (blobshashes.Count > 1)
            {
                // Multiple blobs so create hashlist blob to reference them all together
                byte[] hashlist = new byte[blobshashes.Count * blobshashes[0].Length];
                for (int i = 0; i < blobshashes.Count; i++)
                {
                    Array.Copy(blobshashes[i], 0, hashlist, i * blobshashes[i].Length, blobshashes[i].Length);
                }
                AddMultiBlobReferenceBlob(backupset, filehash, hashlist, type);
            }
            else
            {
                // Just the one blob, so change its type to FileBlob
                GetBlobLocation(filehash).BlobType = type; // filehash should match individual blob hash used earlier since total file == single blob
            }
            return filehash;
        }

        public byte[] RetrieveData(byte[] filehash)
        {
            BlobLocation blobbl = GetBlobLocation(filehash);
            if (blobbl.IsMultiBlobReference) // File is comprised of multiple blobs
            {
                var blobhashes = GetHashListFromBlob(blobbl);

                MemoryStream file = new MemoryStream();
                foreach (var hash in blobhashes)
                {
                    BlobLocation blobloc = GetBlobLocation(hash);
                    file.Write(LoadBlob(blobloc), 0, blobloc.ByteLength);
                }
                byte[] filedata = file.ToArray();
                file.Close();
                return filedata;
            }
            else // file is single blob
            {
                return LoadBlob(blobbl);
            }
        }


        public void CacheBlobList(string backupsetname, BlobStore cacheblobs)
        {
            string bloblistcachebsname = backupsetname + Core.BlobListCacheSuffix;
            cacheblobs.RemoveAllBackupSetReferences(bloblistcachebsname);
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in GetAllHashesAndBlobLocations(backupsetname))
            {
                var bloc = new BlobLocation(hashblob.Value.BlobType, hashblob.Value.IsMultiBlobReference, "", 0, hashblob.Value.ByteLength);
                bloc.BSetReferenceCounts[bloblistcachebsname] = 1;
                cacheblobs.AddBlob(bloblistcachebsname, new HashBlobPair(hashblob.Key, null), hashblob.Value.BlobType, false, true);
            }
        }

        /// <summary>
        /// Loads the data from a blob, no special handling of multiblob references etc.
        /// </summary>
        /// <param name="blocation"></param>
        /// <returns></returns>
        private byte[] LoadBlob(BlobLocation blocation)
        {
            return Dependencies.LoadBlob(blocation.RelativeFilePath, blocation.BytePosition, blocation.ByteLength);
        }

        public void IncrementReferenceCount(string backupsetname, byte[] blobhash, int amount, bool includefiles)
        {
            foreach (var reference in GetAllBlobReferences(blobhash, includefiles))
            {
                IncrementReferenceCountNoRecurse(backupsetname, reference, amount);
            }
            IncrementReferenceCountNoRecurse(backupsetname, blobhash, amount); // must delete parent last so parent can be loaded/used in GetAllBlobReferences()
        }

        private void IncrementReferenceCountNoRecurse(string backupset, byte[] blobhash, int amount) => IncrementReferenceCountNoRecurse(backupset, GetBlobLocation(blobhash), blobhash, amount);

        private void IncrementReferenceCountNoRecurse(string backupset, BlobLocation blocation, byte[] blobhash, int amount)
        {
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

            if (blocation.TotalNonShallowReferenceCount == 0)
            {
                try
                {
                    Dependencies.DeleteBlob(blocation.RelativeFilePath, blocation.BytePosition, blocation.ByteLength);
                }
                catch (Exception)
                {
                    throw new Exception("Error deleting unreferenced file.");
                }
                if (blocation.TotalReferenceCount == 0)
                {
                    IndexStore.Remove(blobhash);
                }
            }
        }

        public void TransferBackup(BlobStore dst, string dstbackupset, byte[] bblobhash, bool includefiles)
        {
            TransferBlobAndReferences(dst, dstbackupset, bblobhash, includefiles);
        }

        private void TransferBlobAndReferences(BlobStore dst, string dstbackupset, byte[] blobhash, bool includefiles)
        {
            if (!TransferBlobNoReferences(dst, dstbackupset, blobhash, includefiles))
            {
                TransferFromBlobReferenceIterator(dst, dstbackupset, GetAllBlobReferences(blobhash, includefiles, false), includefiles);
            }
        }

        private void TransferFromBlobReferenceIterator(BlobStore dst, string backupset, IBlobReferenceIterator references, bool includefiles)
        {
            foreach (var reference in references)
            {
                if (TransferBlobNoReferences(dst, backupset, reference, includefiles))
                {
                    references.SkipChild();
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="blobhash"></param>
        /// <returns>True Blob exists in destination</returns>
        private bool TransferBlobNoReferences(BlobStore dst, string dstbackupset, byte[] blobhash, bool includefiles)
        {
            bool existsindst = dst.ContainsHash(blobhash);
            if (existsindst)
            {
                dst.IncrementReferenceCount(dstbackupset, blobhash, 1, includefiles);
            }
            else
            {
                BlobLocation bloc = GetBlobLocation(blobhash);
                dst.AddBlob(dstbackupset, new HashBlobPair(blobhash, LoadBlob(bloc)), bloc.BlobType);
            }
            return existsindst;
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
        /// Add list of blobs to blobstore. Automatically creates a reference blob (hashlist) if blobs.count > 1
        /// </summary>
        /// <param name="hash"></param>
        /// <param name="blobs"></param>
        /// <param name="type"></param>
        private void AddBlob(string backupset, byte[] hash, List<HashBlobPair> blobs, BlobLocation.BlobTypes type)
        {
            if (blobs.Count == 1)
            {
                AddBlob(backupset, blobs[0], type);
            }
            else
            {
                byte[] hashlist = new byte[blobs[0].Hash.Length * blobs.Count];
                for (int i = 0; i < blobs.Count; i++)
                {
                    AddBlob(backupset, blobs[i], BlobLocation.BlobTypes.Simple, false);
                    Array.Copy(blobs[i].Hash, 0, hashlist, blobs[0].Hash.Length * i, blobs[0].Hash.Length);
                }
                AddMultiBlobReferenceBlob(backupset, hash, hashlist, type);
            }
        }

        /// <summary>
        /// Add a single blob to blobstore.
        /// </summary>
        /// <param name="blob"></param>
        /// <param name="type"></param>
        /// <returns>The BlobLocation the blob is saved to.</returns>
        private BlobLocation AddBlob(string backupset, HashBlobPair blob, BlobLocation.BlobTypes type)
        {
            return AddBlob(backupset, blob, type, false);
        }

        private BlobLocation AddBlob(string backupset, HashBlobPair blob, BlobLocation.BlobTypes type, bool isMultiblobReference, bool shallow=false)
        {
            string relpath = HashTools.ByteArrayToHexViaLookup32(blob.Hash);

            // We navigate down 

            // Where we will put the blob data if we dont already have it stored
            BlobLocation posblocation;
            if (shallow)
            {
                posblocation = new BlobLocation(type, isMultiblobReference, relpath, 0, 0);
            }
            else
            {
                posblocation = new BlobLocation(type, isMultiblobReference, relpath, 0, blob.Block.Length);
            }
             

            // Where the data is already stored if it exists
            (BlobLocation bloc, bool datastored)? existingblocstored;
            lock (this)
            {
                // Have we already stored this?
                existingblocstored = AddHash(blob.Hash, posblocation);
            }
            if (existingblocstored == null)
            {
                if (!shallow)
                {
                    WriteBlob(blob.Hash, blob.Block, posblocation);
                }
                IncrementReferenceCountNoRecurse(backupset, posblocation, blob.Hash, 1);
                return posblocation;
            }
            else
            {
                (BlobLocation existingbloc, bool datastored) = existingblocstored.Value;
                // Is the data not already stored in the blobstore (are all references shallow thus far)?
                if (!datastored)
                {
                    // Data is not already stored
                    // If we are saving to a cache and the bloblist cache indicates the destination has the data
                    // Then dont store, Else save
                    if (!(backupset.EndsWith(Core.CacheSuffix) 
                            && existingbloc.BSetReferenceCounts.ContainsKey(backupset.Substring(0, 
                                backupset.Length - Core.CacheSuffix.Length) + Core.BlobListCacheSuffix)))
                    {
                        WriteBlob(blob.Hash, blob.Block, existingbloc);
                    }
                }
                IncrementReferenceCountNoRecurse(backupset, existingbloc, blob.Hash, 1);
                return existingbloc;
            }
        }

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

        private void AddMultiBlobReferenceBlob(string backupset, byte[] hash, byte[] hashlist, BlobLocation.BlobTypes type)
        {
            HashBlobPair referenceblob = new HashBlobPair(hash, hashlist);
            AddBlob(backupset, referenceblob, type, true);
        }

        public IBlobReferenceIterator GetAllBlobReferences(byte[] blobhash, bool includefiles, bool bottomup=true)
        {
            return new BlobReferenceIterator(this, blobhash, includefiles, bottomup);
        }

        private void WriteBlob(byte[] hash, byte[] blob, BlobLocation blobLocation)
        {
            (string relfilepath, int bytepos) = Dependencies.StoreBlob(hash, blob);
            blobLocation.RelativeFilePath = relfilepath;
            blobLocation.BytePosition = bytepos;
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
            return IndexStore.GetRecord(hash);
        }

        private List<byte[]> GetHashListFromBlob(BlobLocation blocation)
        {
            if (!blocation.IsMultiBlobReference)
            {
                throw new ArgumentException("blobhash must be of a blob with IsMultiBlockReference=true");
            }
            try
            {
                byte[] hashlistblob = LoadBlob(blocation);
                List<byte[]> blobhashes = new List<byte[]>();
                for (int i = 0; i < hashlistblob.Length / 20; i++)
                {
                    byte[] hash = new byte[20];
                    Array.Copy(hashlistblob, i * 20, hash, 0, 20);
                    blobhashes.Add(hash);
                }
                return blobhashes;
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to read hashlist blob");
                throw;
            }
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
        public (int allreferences, int uniquereferences) GetSizes(byte[] blobhash)
        {
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize = new Dictionary<string, (int, BlobLocation)>();
            GetBlobReferenceFrequencies(blobhash, hashfreqsize);
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

        private void GetBlobReferenceFrequencies(byte[] blobhash, Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize) // TODO: use something better than object[] (currently used because tuples are readonly)
        {
            GetReferenceFrequenciesNoRecurse(blobhash, hashfreqsize);
            foreach (var reference in GetAllBlobReferences(blobhash, true))
            {
                GetReferenceFrequenciesNoRecurse(reference, hashfreqsize);
            }
        }

        private void GetReferenceFrequenciesNoRecurse(byte[] blobhash, Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize)
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

            public BlobReferenceIterator(BlobStore blobs, byte[] blobhash, bool includefiles, bool bottomup)
            {
                Blobs = blobs;
                ParentHash = blobhash;
                BottomUp = bottomup;
                IncludeFiles = includefiles;
            }

            public void SkipChild()
            {
                skipchild = true;
                if (childiterator != null)
                {
                    childiterator.SkipChild();
                }
                if (BottomUp)
                {
                    throw new Exception("Skip child is not valid when iterating through references top down.");
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
                switch (blocation.BlobType)
                {
                    case BlobLocation.BlobTypes.Simple:
                        break;
                    case BlobLocation.BlobTypes.FileBlob:
                        break;
                    case BlobLocation.BlobTypes.BackupRecord:
                        BackupRecord br = BackupRecord.deserialize(Blobs.RetrieveData(ParentHash));
                        if (!BottomUp)
                        {
                            skipchild = false;
                            yield return br.MetadataTreeHash; // return 1 immediate reference
                        }
                        if (!skipchild)
                        {
                            childiterator = new BlobReferenceIterator(Blobs, br.MetadataTreeHash, IncludeFiles, BottomUp);
                            foreach (var refref in childiterator) // recurse on references of that reference
                            {
                                skipchild = false;
                                yield return refref;
                            }
                            childiterator = null;
                        }
                        if (BottomUp)
                        {
                            skipchild = false;
                            yield return br.MetadataTreeHash; // return 1 immediate reference
                        }
                        break;
                    case BlobLocation.BlobTypes.MetadataNode:
                        IEnumerable<byte[]> dirreferences;
                        IEnumerable<byte[]> filereferences = null;
                        dirreferences = MetadataNode.GetImmediateChildNodeReferencesWithoutLoad(Blobs.RetrieveData(ParentHash)); // many immediate references
                        if (IncludeFiles)
                        {
                            filereferences = MetadataNode.GetImmediateFileReferencesWithoutLoad(Blobs.RetrieveData(ParentHash));
                            foreach (var fref in filereferences)
                            {
                                if (!BottomUp)
                                {
                                    skipchild = false;
                                    yield return fref;
                                }
                                if (!skipchild)
                                {
                                    childiterator = new BlobReferenceIterator(Blobs, fref, IncludeFiles, BottomUp);
                                    foreach (var frefref in childiterator)
                                    {
                                        skipchild = false;
                                        yield return frefref;
                                    }
                                    childiterator = null;
                                }
                                if (BottomUp)
                                {
                                    skipchild = false;
                                    yield return fref;
                                }
                            }
                        }
                        foreach (var reference in dirreferences) // for each immediate reference
                        {
                            if (!BottomUp)
                            {
                                skipchild = false;
                                yield return reference; // return immediate reference
                            }
                            if (!skipchild)
                            {
                                childiterator = new BlobReferenceIterator(Blobs, reference, IncludeFiles, BottomUp);
                                foreach (var refref in childiterator) // recurse on references of that reference
                                {
                                    skipchild = false;
                                    yield return refref;
                                }
                            }
                            childiterator = null;
                            if (BottomUp)
                            {
                                skipchild = false;
                                yield return reference; // return immediate reference
                            }
                        }
                        break;
                    default:
                        throw new Exception("Unhandled blobtype on dereference");
                }
                if (blocation.IsMultiBlobReference)
                {
                    foreach (var hash in Blobs.GetHashListFromBlob(blocation))
                    {
                        skipchild = false;
                        yield return hash;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        public interface IBlobReferenceIterator : IEnumerable<byte[]>
        {
            void SkipChild();
        }
    }
}
