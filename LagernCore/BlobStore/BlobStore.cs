﻿using BackupCore.Models;
using BackupCore.Utilities;
using LagernCore.Models;
using LagernCore.Utilities;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace BackupCore
{
    /// <summary>
    /// Binary tree holding hashes and their corresponding locations in backup
    /// </summary>
    public class BlobStore : ICustomSerializable, ICustomDeserializableWithDependencies<BlobStore, IBlobStoreDependencies>
    {
        public BPlusTree<BlobLocation> IndexStore { get; private set; }

        public IBlobStoreDependencies Dependencies { get; set; }

        public BlobStore(IBlobStoreDependencies dependencies)
        {
            IndexStore = new BPlusTree<BlobLocation>(100);
            Dependencies = dependencies;
        }

        /// <summary>
        /// Store some binary data into some blobstores.
        /// Does not increment any blob references. Call FinalizeBlobAddition() to complete addition.
        /// </summary>
        /// <param name="blobStores"></param>
        /// <param name="backupset"></param>
        /// <param name="inputdata"></param>
        /// <returns></returns>
        public static async Task<byte[]> StoreData(IEnumerable<BlobStore> blobStores, BackupSetReference backupset, byte[] inputdata)
        {
            return await StoreData(blobStores, backupset, new MemoryStream(inputdata));
        }

        /// <summary>
        /// Store some binary data into some blobstores.
        /// Does not increment any blob references.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        public static async Task<byte[]> StoreData(IEnumerable<BlobStore> blobStores, BackupSetReference backupset, Stream readerbuffer)
        {
            BlockingCollection<HashBlobPair> fileblobqueue = new();
            byte[] filehash = new byte[20]; // Overall hash of file
            SplitData(readerbuffer, filehash, fileblobqueue);

            List<byte[]> blobshashes = new();
            while (!fileblobqueue.IsCompleted)
            {
                if (fileblobqueue.TryTake(out HashBlobPair? blob))
                {
                    await Task.WhenAll(blobStores.Select(bs => bs.AddBlob(backupset, blob)));
                    blobshashes.Add(blob.Hash);
                }
            }
            if (blobshashes.Count > 1)
            {
                // Multiple blobs so create hashlist reference to reference them all together
                await Task.WhenAll(blobStores.Select(bs => bs.AddMultiBlobReferenceBlob(backupset, filehash, blobshashes)));
            }
            return filehash;
        }

        public async Task<byte[]> RetrieveData(byte[] filehash)
        {
            BlobLocation blobbl = GetBlobLocation(filehash);
            return await RetrieveData(filehash, blobbl);
        }

        public async Task<byte[]> RetrieveData(byte[] filehash, BlobLocation blobLocation)
        {
            if (blobLocation.BlockHashes != null) // File is comprised of multiple blobs
            {
                MemoryStream outstream = new();
                foreach (var hash in blobLocation.BlockHashes)
                {
                    BlobLocation blobloc = GetBlobLocation(hash);
                    outstream.Write(await LoadBlob(blobloc, hash), 0, blobloc.ByteLength);
                }
                byte[] filedata = outstream.ToArray();
                outstream.Close();
                return filedata;
            }
            else // file is single blob
            {
                return await LoadBlob(blobLocation, filehash);
            }
        }

        public async Task CacheBlobList(string backupsetname, BlobStore cacheblobs)
        {
            BackupSetReference bloblistcachebsname = new(backupsetname, true, false, true);
            await cacheblobs.RemoveAllBackupSetReferences(bloblistcachebsname);
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in GetAllHashesAndBlobLocations(new BackupSetReference(backupsetname, true, false, false)))
            {
                await cacheblobs.AddBlob(bloblistcachebsname, new HashBlobPair(hashblob.Key, null), hashblob.Value.BlockHashes, true);
            }
        }

        /// <summary>
        /// Loads the data from a blob, no special handling of multiblob references etc.
        /// </summary>
        /// <param name="blocation"></param>
        /// <param name="hash">Null for no verification</param>
        /// <returns></returns>
        private async Task<byte[]> LoadBlob(BlobLocation blocation, byte[] hash, int retries=0)
        {
            if (blocation.EncryptedHash == null)
            {
                throw new Exception("Hash must not be null");
            }

            /* // Addititional correctness checks
            byte[] encryptedData = Dependencies.LoadBlob(blocation.EncryptedHash, false);
            byte[] encDatahash = HashTools.GetSHA1Hasher().ComputeHash(encryptedData);
            if (!encDatahash.SequenceEqual(blocation.EncryptedHash))
            {
                throw new Exception("Encrypted hash did not match");
            }*/

            byte[] data = await Dependencies.LoadBlob(blocation.EncryptedHash);
            byte[] datahash = HashTools.GetSHA1Hasher().ComputeHash(data);
            if (datahash.SequenceEqual(hash))
            {
                return data;
            }
            else if (retries > 0)
            {
                return await LoadBlob(blocation, hash, retries - 1);
            }
            // NOTE: This hash check sometimes fails and throws the error, Issue #17
            //  Possibly resolved as of 2b15b7cb
            throw new Exception("Blob data did not match hash.");
        }

        public async Task DecrementReferenceCount(BackupSetReference backupsetname, byte[] blobhash, BlobLocation.BlobType blobtype,
            bool includefiles)
        {
            BlobLocation rootBlobLocation = GetBlobLocation(blobhash);
            if (rootBlobLocation.GetBSetReferenceCount(backupsetname) == 1) // To be deleted?
            {
                IBlobReferenceIterator blobReferences = GetAllBlobReferences(blobhash, blobtype, includefiles, false);
                await foreach (var reference in blobReferences)
                {
                    BlobLocation blocation = GetBlobLocation(reference);

                    // When we finish iterating over the children, decrement this blob
                    blobReferences.PostOrderAction(() => IncrementReferenceCountNoRecurse(backupsetname, blocation, reference, -1));
                    try
                    {
                        if (blocation.GetBSetReferenceCount(backupsetname) != 1) // Not to be deleted?
                        {
                            // Dont need to decrement child references if this wont be deleted
                            blobReferences.SkipChildrenOfCurrent();
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                        throw;
                    }
                }
            }
            await IncrementReferenceCountNoRecurse(backupsetname, rootBlobLocation, blobhash, -1); // must delete parent last so parent can be loaded/used in GetAllBlobReferences()
        }

        private async Task IncrementReferenceCountNoRecurse(BackupSetReference backupset, byte[] blobhash, int amount) => 
            await IncrementReferenceCountNoRecurse(backupset, GetBlobLocation(blobhash), blobhash, amount);

        private async Task IncrementReferenceCountNoRecurse(BackupSetReference backupset, BlobLocation blocation, byte[] blobhash, int amount)
        {
            bool originallyshallow = blocation.TotalNonShallowReferenceCount == 0;
            int? refCount = blocation.GetBSetReferenceCount(backupset);
            int newRefCount = refCount.GetValueOrDefault(0) + amount;
            blocation.SetBSetReferenceCount(backupset, newRefCount);
            if (newRefCount == 0)
            {
                blocation.RemoveBSetReference(backupset);
            }
            else if (newRefCount < 0)
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
                            if (blocation.EncryptedHash == null)
                            {
                                throw new Exception("Hash should not be null");
                            }
                            await Dependencies.DeleteBlob(blocation.EncryptedHash, blocation.RelativeFilePath);
                        }
                        catch (Exception e)
                        {
                            throw new Exception("Error deleting unreferenced file.", e);
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
        public async Task TransferBackup(BlobStore dst, BackupSetReference dstbackupset, byte[] bblobhash, bool includefiles)
        {
            await TransferBlobAndReferences(dst, dstbackupset, bblobhash, BlobLocation.BlobType.BackupRecord, includefiles);
        }

        // TODO: If include files is false, should we require dstbackupset.EndsWith(Core.ShallowSuffix)?
        public async Task TransferBlobAndReferences(BlobStore dst, BackupSetReference dstbackupset, byte[] blobhash, 
            BlobLocation.BlobType blobtype, bool includefiles)
        {
            bool refInDst;
            bool shallowInDst;
            BlobLocation? rootDstBlobLocation = null;
            try
            {
                rootDstBlobLocation = dst.GetBlobLocation(blobhash);
                refInDst = true;
                shallowInDst = rootDstBlobLocation.TotalNonShallowReferenceCount == 0;
            }
            catch (KeyNotFoundException)
            {
                refInDst = false;
                shallowInDst = false; // Meaningless when ref not in dst
            }

            if (!refInDst || (shallowInDst && includefiles))
            {
                byte[]? blob;
                if (refInDst)
                {
                    blob = await RetrieveData(blobhash);
                }
                else
                {
                    (rootDstBlobLocation, blob) = await TransferBlobNoReferences(dst, dstbackupset, blobhash, GetBlobLocation(blobhash));
                }

                IBlobReferenceIterator blobReferences = GetAllBlobReferences(blobhash, blobtype, includefiles, false);
                blobReferences.SupplyData(blob);
                await foreach (var reference in blobReferences)
                {
                    bool iterRefInDst;
                    bool iterShallowInDst;
                    BlobLocation? dstBlobLocation = null;
                    try
                    {
                        dstBlobLocation = dst.GetBlobLocation(reference);
                        iterRefInDst = true;
                        iterShallowInDst = dstBlobLocation.TotalNonShallowReferenceCount == 0;
                    }
                    catch (KeyNotFoundException)
                    {
                        iterRefInDst = false;
                        iterShallowInDst = false; // Meaningless when ref not in dst
                    }

                    if (!iterRefInDst || (iterShallowInDst && includefiles))
                    {
                        if (iterRefInDst)
                        {
                            blob = await RetrieveData(reference);
                        }
                        else
                        {
                            var srcBlobLocation = GetBlobLocation(reference);
                            (dstBlobLocation, blob) = await TransferBlobNoReferences(dst, dstbackupset, reference, srcBlobLocation);
                        }
                        blobReferences.SupplyData(blob);
                    } 
                    else
                    {
                        // Dont need to increment child references if this already exists
                        blobReferences.SkipChildrenOfCurrent();
                    }

                    // When we finish iterating over the children, increment this blob
#pragma warning disable CS8604 // Possible null reference argument.
                    blobReferences.PostOrderAction(() => dst.IncrementReferenceCountNoRecurse(dstbackupset, dstBlobLocation, reference, 1));
#pragma warning restore CS8604 // Possible null reference argument.
                }
            }

#pragma warning disable CS8604 // Possible null reference argument.
            await dst.IncrementReferenceCountNoRecurse(dstbackupset, rootDstBlobLocation, blobhash, 1);
#pragma warning restore CS8604 // Possible null reference argument.
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dst"></param>
        /// <param name="blobhash"></param>
        /// <returns>True Blob exists in destination</returns>
        private async Task<(BlobLocation bloc, byte[]? blob)> TransferBlobNoReferences(BlobStore dst, BackupSetReference dstbackupset,
            byte[] blobhash, BlobLocation blocation)
        {
            if (blocation.BlockHashes == null)
            {
                byte[] blob = await LoadBlob(blocation, blobhash);
                return (await dst.AddBlob(dstbackupset, new HashBlobPair(blobhash, blob)), blob);
            }
            else
            {
                return (await dst.AddMultiBlobReferenceBlob(dstbackupset, blobhash, blocation.BlockHashes), null);
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
            BlobLocation? existingblocation = IndexStore.AddOrFind(hash, blocation);
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
        private async Task<BlobLocation> AddBlob(BackupSetReference backupset, HashBlobPair blob)
        {
            return await AddBlob(backupset, blob, null);
        }

        private async Task<BlobLocation> AddBlob(BackupSetReference backupset, HashBlobPair blob, List<byte[]>? blockreferences, bool shallow=false)
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
                    if (blob.Block == null)
                    {
                        throw new Exception("Block can only be null in multirefernce blob");
                    }
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
                        if (blob.Block == null)
                        {
                            throw new Exception("Block can only be null in multirefernce blob");
                        }
                        (posblocation.EncryptedHash, posblocation.RelativeFilePath) = await WriteBlob(blob.Hash, blob.Block);
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
                        if (!backupset.BlobListCache)
                        {
                            // If we are saving to a cache and the bloblist cache indicates the destination has the data
                            // Then dont store, Else save
                            //BackupSetReference blobListCacheReference = backupset with { BlobListCache = true };
                            if (!(backupset.Cache
                                && existingbloc.GetBSetReferenceCount(backupset).HasValue))
                            {
                                if (blob.Block == null)
                                {
                                    throw new Exception("Block can only be null in multirefernce blob");
                                }
                                (existingbloc.EncryptedHash, existingbloc.RelativeFilePath) = await WriteBlob(blob.Hash, blob.Block);
                            }
                        }
                    }
                }
                // Dont change reference counts until finalization
                // IncrementReferenceCountNoRecurse(backupset, existingbloc, blob.Hash, 1);
                return existingbloc;
            }
        }

        /// <summary>
        /// Finalizes
        /// 1. The backup record itself
        /// 2. The metadata tree 
        /// 3. All contained files
        /// 
        /// The hashes for #2 (excep root) and #3 are provided in the HashTreeNode and its children.
        /// </summary>
        /// <param name="bsname"></param>
        /// <param name="backuphash"></param>
        /// <param name="mtreehash"></param>
        /// <param name="mtreereferences">A tree of hashes. See issue #46 for why this was needed.</param>
        public async Task FinalizeBackupAddition(BackupSetReference bsname, byte[] backuphash, byte[] mtreehash, HashTreeNode mtreereferences)
        {
            BlobLocation backupblocation = GetBlobLocation(backuphash);
            int? backupRefCount = backupblocation.GetBSetReferenceCount(bsname);
            if (!backupRefCount.HasValue || backupRefCount == 0)
            {
                BlobLocation mtreeblocation = GetBlobLocation(mtreehash);
                int? mtreeRefCount = mtreeblocation.GetBSetReferenceCount(bsname);
                if (!mtreeRefCount.HasValue || mtreeRefCount == 0)
                {
                    ISkippableChildrenEnumerable<byte[]> childReferences = mtreereferences.GetChildIterator(); // This only iterates through nested nodes, no nested files
                    foreach (var blobhash in childReferences)
                    {
                        BlobLocation blocation = GetBlobLocation(blobhash);
                        int? refCount = blocation.GetBSetReferenceCount(bsname);
                        if (refCount.HasValue && refCount > 0) // This was already stored
                        {
                            childReferences.SkipChildrenOfCurrent();
                        }
                        else if (blocation.BlockHashes != null)
                        {
                            foreach (var mbref in blocation.BlockHashes)
                            {
                                await IncrementReferenceCountNoRecurse(bsname, mbref, 1);
                            }
                        }
                        await IncrementReferenceCountNoRecurse(bsname, blocation, blobhash, 1);
                    }
                    await IncrementReferenceCountNoRecurse(bsname, mtreeblocation, mtreehash, 1);
                }
                await IncrementReferenceCountNoRecurse(bsname, backupblocation, backuphash, 1);
            }
        }

        [Obsolete("See issue #46 for why we switched to FinalizeBackupAddition() which uses a tree of hashes")] // TODO: Update tests to new method and delete this
        public async Task FinalizeBlobAddition(BackupSetReference bsname, byte[] blobhash, BlobLocation.BlobType blobType)
        {
            // Handle root blob
            BlobLocation rootblocation = GetBlobLocation(blobhash);
            if (rootblocation.TotalReferenceCount == 0)
            {
                IBlobReferenceIterator blobReferences = GetAllBlobReferences(blobhash, blobType, true, false);
                // Loop through children
                await foreach (byte[] reference in blobReferences)
                {
                    BlobLocation blocation = GetBlobLocation(reference);
                    if (blocation.TotalReferenceCount > 0) // This was already stored
                    {
                        blobReferences.SkipChildrenOfCurrent();
                    }
                    await IncrementReferenceCountNoRecurse(bsname, blocation, blobhash, 1);
                }
            }
            // Increment root blob
            await IncrementReferenceCountNoRecurse(bsname, rootblocation, blobhash, 1);
        }

        // TODO: should we just have to iteratively remove each backup in the bset?
        public async Task RemoveAllBackupSetReferences(BackupSetReference bsname)
        {
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in IndexStore)
            {
                int? refCount = hashblob.Value.GetBSetReferenceCount(bsname);
                if (refCount != null)
                {
                    await IncrementReferenceCountNoRecurse(bsname, hashblob.Key, -refCount.Value);
                }
            }
        }

        private async Task<BlobLocation> AddMultiBlobReferenceBlob(BackupSetReference backupset, byte[] hash, List<byte[]> hashlist)
        {
            HashBlobPair referenceblob = new(hash, null);
            return await AddBlob(backupset, referenceblob, hashlist);
        }

        public IBlobReferenceIterator GetAllBlobReferences(byte[] blobhash, BlobLocation.BlobType blobtype,
            bool includefiles, bool bottomup=true)
        {
            return new BlobReferenceIterator(this, blobhash, blobtype, includefiles, bottomup);
        }

        private async Task<(byte[] encryptedHash, string fileId)> WriteBlob(byte[] hash, byte[] blob)
        {
            return await Dependencies.StoreBlob(hash, blob);
        }

        public bool ContainsHash(byte[] hash)
        {
            return IndexStore.GetRecord(hash) != null;
        }

        public bool ContainsHash(BackupSetReference backupset, byte[] hash)
        {
            BlobLocation? blocation = IndexStore.GetRecord(hash);
            if (blocation != null)
            {
                return blocation.GetBSetReferenceCount(backupset).HasValue;
            }
            return false;
        }

        public BlobLocation GetBlobLocation(byte[] hash)
        {
            BlobLocation? blocation = IndexStore.GetRecord(hash);
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
        public static void SplitData(byte[] inputdata, byte[] filehash, BlockingCollection<HashBlobPair> hashblobqueue)
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
        public static void SplitData(Stream inputstream, byte[] filehash, BlockingCollection<HashBlobPair> hashblobqueue)
        {
            // https://rsync.samba.org/tech_report/node3.html
            List<byte> newblob = new();
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
                        HashTools.ByteSum(alphachksum, newblob[^1]);
                        if (newblob.Count > rollwindowsize)
                        {
                            HashTools.ByteDifference(alphachksum, newblob[newblob.Count - rollwindowsize - 1]);
                            shifted[0] = (byte)((newblob[^1] << 5) & 0xFF); // rollwindowsize = 32 = 2^5 => 5
                            shifted[1] = (byte)((newblob[^1] >> 3) & 0xFF); // 8-5 = 3
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
                byte[] blob = Array.Empty<byte>();
                sha1filehasher.TransformFinalBlock(blob, 0, blob.Length);
                hashblobqueue.Add(new HashBlobPair(sha1blobhasher.ComputeHash(blob), blob));
            }
            if (sha1filehasher.Hash == null)
            {
                throw new NullReferenceException("Hash cannot be null here");
            }
            Array.Copy(sha1filehasher.Hash, filehash, sha1filehasher.Hash.Length);
            hashblobqueue.CompleteAdding();
        }

        /// <summary>
        /// Calculates the size of the blobs and child blobs referenced by the given hash.
        /// </summary>
        /// <param name="blobhash"></param>
        /// <returns>(Size of all referenced blobs, size of blobs referenced only by the given hash and its children)</returns>
        public async Task<(int allreferences, int uniquereferences)> GetSizes(byte[] blobhash, BlobLocation.BlobType blobtype)
        {
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize = new();
            await GetBlobReferenceFrequencies(blobhash, blobtype, hashfreqsize);
            int allreferences = 0;
            int uniquereferences = 0;
            foreach (var (frequency, blocation) in hashfreqsize.Values)
            {
                allreferences += blocation.ByteLength * frequency;
                if (blocation.TotalReferenceCount == frequency)
                {
                    uniquereferences += blocation.ByteLength; // TODO: unique referenes 
                }
            }
            return (allreferences, uniquereferences);
        }

        private async Task GetBlobReferenceFrequencies(byte[] blobhash, BlobLocation.BlobType blobtype, 
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize) 
        {
            GetReferenceFrequenciesNoRecurse(blobhash, hashfreqsize);
            await foreach (var reference in GetAllBlobReferences(blobhash, blobtype, true))
            {
                GetReferenceFrequenciesNoRecurse(reference, hashfreqsize);
            }
        }

        private void GetReferenceFrequenciesNoRecurse(byte[] blobhash, 
            Dictionary<string, (int frequency, BlobLocation blocation)> hashfreqsize)
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

        private IEnumerable<KeyValuePair<byte[], BlobLocation>> GetAllHashesAndBlobLocations(BackupSetReference bsname)
        {
            foreach (KeyValuePair<byte[], BlobLocation> hashblob in IndexStore)
            {
                if (hashblob.Value.GetBSetReferenceCount(bsname).HasValue)
                {
                    yield return hashblob;
                }
            }
        }

        public byte[] Serialize()
        {
            Dictionary<string, byte[]> bptdata = new();
            // -"-v1"
            // keysize = BitConverter.GetBytes(int) (only used for decoding HashBLocationPairs)
            // HashBLocationPairs = enum_encode(List<byte[]> [hash,... & backuplocation.serialize(),...])
            // -"-v2"
            // IsCache = BitConverter.GetBytes(bool)
            // -"-v3"
            // Removed IsCache

            bptdata.Add("keysize-v1", BitConverter.GetBytes(20));

            List<byte[]> binkeyvals = new();
            foreach (KeyValuePair<byte[], BlobLocation> kvp in IndexStore)
            {
                byte[] keybytes = kvp.Key;
                byte[] backuplocationbytes = kvp.Value.Serialize();
                byte[] binkeyval = new byte[keybytes.Length + backuplocationbytes.Length];
                Array.Copy(keybytes, binkeyval, keybytes.Length);
                Array.Copy(backuplocationbytes, 0, binkeyval, keybytes.Length, backuplocationbytes.Length);
                binkeyvals.Add(binkeyval);
            }
            bptdata.Add("HashBLocationPairs-v1", BinaryEncoding.EnumEncode(binkeyvals));
            
            return BinaryEncoding.dict_encode(bptdata);
        }

        private static IEnumerable<KeyValuePair<byte[], BlobLocation>> DeconstructHashBlocationPairs(byte[] hblp, int keysize)
        {
            var hashblocationpairs = BinaryEncoding.enum_decode(hblp);
            if (hashblocationpairs == null)
            {
                throw new Exception("Hash blocation pairs cannot be null");
            }
            foreach (byte[]? binkvp in hashblocationpairs)
            {
                if (binkvp == null)
                {
                    throw new Exception("Hash blocation pairs cannot be null");
                }
                byte[] keybytes = new byte[keysize];
                byte[] backuplocationbytes = new byte[binkvp.Length - keysize];
                Array.Copy(binkvp, keybytes, keysize);
                Array.Copy(binkvp, keysize, backuplocationbytes, 0, binkvp.Length - keysize);

                yield return new KeyValuePair<byte[], BlobLocation>(keybytes, BlobLocation.Deserialize(backuplocationbytes));
            }
        }

        public static BlobStore Deserialize(byte[] data, IBlobStoreDependencies dependencies)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            int keysize = BitConverter.ToInt32(savedobjects["keysize-v1"], 0);

            BlobStore bs = new(dependencies);
            bs.IndexStore = new BPlusTree<BlobLocation>(DeconstructHashBlocationPairs(savedobjects["HashBLocationPairs-v1"], keysize),
                bs.IndexStore.NodeSize);
            return bs;
        }

        private class BlobReferenceIterator : IBlobReferenceIterator
        {
            public byte[] ParentHash { get; set; }

            public BlobStore Blobs { get; set; }

            private bool IncludeFiles { get; set; }

            public bool BottomUp { get; set; } // Determines whether or not to return child references first

            private byte[]? BlobData { get; set; }

            private BlobLocation.BlobType BlobType { get; set; }

            private bool skipchild = false;
            private IBlobReferenceIterator? childiterator = null;
            private Func<Task>? postOrderAction = null;
            private BlobLocation.BlobType? justReturnedType;

            public BlobReferenceIterator(BlobStore blobs, byte[] blobhash, BlobLocation.BlobType blobtype,
                bool includefiles, bool bottomup)
            {
                Blobs = blobs;
                ParentHash = blobhash;
                BottomUp = bottomup;
                IncludeFiles = includefiles;
                BlobType = blobtype;
            }

            public BlobLocation.BlobType? JustReturnedType
            {
                get
                {
                    if (childiterator != null)
                    {
                        //return childiterator.JustReturnedType;
                        return null;
                    }
                    else
                    {
                        return justReturnedType;
                    }
                }
                set
                {
                    justReturnedType = value;
                }
            }

            public void SkipChildrenOfCurrent()
            {
                if (childiterator != null)
                {
                    childiterator.SkipChildrenOfCurrent();
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

            public void PostOrderAction(Func<Task> action)
            {
                if (BottomUp)
                {
                    throw new Exception("Post order action is not valid when iterating through references bottum up.");
                }
                if (childiterator != null)
                {
                    childiterator.PostOrderAction(action);
                }
                else
                {
                    postOrderAction = action;
                }
            }

            // TODO: Supply data looks like it will fail with BottomUp = true.
            //  Check this and if so throw an error if it is used when it shouldnt be
            public void SupplyData(byte[]? blobdata)
            {
                if (childiterator != null)
                {
                    childiterator.SupplyData(blobdata);
                }
                else
                {
                    BlobData = blobdata;
                }
            }

            public async IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default)
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
                    default:
                        byte[] blobdata = BlobData ?? await Blobs.RetrieveData(ParentHash);
                        switch (BlobType)
                        {
                            case BlobLocation.BlobType.BackupRecord:
                                BackupRecord br = BackupRecord.Deserialize(blobdata);
                                if (!BottomUp)
                                {
                                    justReturnedType = BlobLocation.BlobType.MetadataNode;
                                    yield return br.MetadataTreeHash; // return 1 immediate reference
                                }
                                if (!skipchild)
                                {
                                    childiterator = new BlobReferenceIterator(Blobs, br.MetadataTreeHash,
                                        BlobLocation.BlobType.MetadataNode, IncludeFiles, BottomUp);
                                    await foreach (var refref in childiterator) // recurse on references of that reference
                                    {
                                        yield return refref;
                                    }
                                    childiterator = null;
                                }
                                skipchild = false;
                                if (BottomUp)
                                {
                                    justReturnedType = BlobLocation.BlobType.MetadataNode;
                                    yield return br.MetadataTreeHash; // return 1 immediate reference
                                }
                                break;
                            case BlobLocation.BlobType.MetadataNode:
                                IEnumerable<byte[]> dirreferences;
                                IEnumerable<byte[]>? filereferences = null;
                                byte[] mnodebytes = blobdata;
                                dirreferences = MetadataNode.GetImmediateChildNodeReferencesWithoutLoad(mnodebytes); // many immediate references
                                if (IncludeFiles)
                                {
                                    filereferences = MetadataNode.GetImmediateFileReferencesWithoutLoad(mnodebytes);
                                    foreach (var fref in filereferences)
                                    {
                                        if (!BottomUp)
                                        {
                                            justReturnedType = BlobLocation.BlobType.FileBlob;
                                            yield return fref;
                                        }
                                        if (!skipchild)
                                        {
                                            childiterator = new BlobReferenceIterator(Blobs, fref, BlobLocation.BlobType.FileBlob,
                                                IncludeFiles, BottomUp);
                                            await foreach (var frefref in childiterator)
                                            {
                                                yield return frefref;
                                            }
                                            childiterator = null;
                                        }
                                        skipchild = false;
                                        if (BottomUp)
                                        {
                                            justReturnedType = BlobLocation.BlobType.FileBlob;
                                            yield return fref;
                                        }
                                    }
                                }
                                foreach (var reference in dirreferences) // for each immediate reference
                                {
                                    if (!BottomUp)
                                    {
                                        justReturnedType = BlobLocation.BlobType.MetadataNode;
                                        yield return reference; // return immediate reference
                                    }
                                    if (!skipchild)
                                    {
                                        childiterator = new BlobReferenceIterator(Blobs, reference, BlobLocation.BlobType.MetadataNode,
                                            IncludeFiles, BottomUp);
                                        await foreach (var refref in childiterator) // recurse on references of that reference
                                        {
                                            yield return refref;
                                        }
                                        childiterator = null;
                                    }
                                    skipchild = false;
                                    if (BottomUp)
                                    {
                                        justReturnedType = BlobLocation.BlobType.MetadataNode;
                                        yield return reference; // return immediate reference
                                    }
                                }
                                break;
                            default:
                                throw new Exception("Unhandled blobtype on dereference");
                        }
                        break;
                }
                if (blocation.BlockHashes != null)
                {
                    var blockHashes = blocation.BlockHashes;
                    childiterator = new BlobReferenceIteratorDelegate(blockHashes);
                    await foreach (var hash in childiterator)
                    {
                        justReturnedType = BlobLocation.BlobType.Simple;
                        yield return hash;
                    }
                    childiterator = null;
                }

                // Run post order action if exists
                if (postOrderAction != null)
                {
                    await postOrderAction.Invoke();
                }
            }
        }

        public class BlobReferenceIteratorDelegate : IBlobReferenceIterator
        {
            private readonly IEnumerable<byte[]> enumerable;

            private Func<Task>? postOrderAction = null;

            public BlobReferenceIteratorDelegate(IEnumerable<byte[]> enumerable)
            {
                this.enumerable = enumerable;
            }

            public async IAsyncEnumerator<byte[]> GetAsyncEnumerator(CancellationToken cancellationToken = default)
            {
                foreach (var item in enumerable)
                {
                    yield return item;
                    if (postOrderAction != null)
                    {
                        await postOrderAction.Invoke();
                    }
                }
            }

            public void PostOrderAction(Func<Task> action)
            {
                postOrderAction = action;
            }

            public void SkipChildrenOfCurrent() { }

            public void SupplyData(byte[]? blobdata) { }
        }

        public interface IPostOrderAsyncActionAsyncEnumerable<T> : IAsyncEnumerable<T>
        {
            void PostOrderAction(Func<Task> action);
        }

        public interface IBlobReferenceIterator : ISkippableChildrenAsyncEnumerable<byte[]>, IPostOrderAsyncActionAsyncEnumerable<byte[]>
        {
            void SupplyData(byte[]? blobdata);
        }
    }
}
