using LagernCore.Models;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BackupStore
    {
        private IBackupStoreDependencies Dependencies { get; set; }

        public BackupStore(IBackupStoreDependencies dependencies)
        {
            Dependencies = dependencies;
        }

        /// <summary>
        /// Syncs a cache with this backup store for the given bsname.
        /// Moves blobs only present in cache to the blobstore tied to this BackupStore
        /// Does not trigger a save of either cache or this backup set
        /// </summary>
        /// <param name="cache"></param>
        /// <param name="bsname"></param>
        /// <param name="dstbset"></param>
        /// <param name="cachebset"></param>
        public async Task<(BackupSet dstbset, BackupSet cachebset)> SyncCache(BackupStore cache, string bsname, BackupSet? dstbset = null, BackupSet? cachebset = null)
        {
            int cacheindex = 0;
            int dstindex = 0;
            BackupRecord? cachebr = null;
            BackupRecord? dstbr = null;

            BackupSetReference cachebsname = new(bsname, false, true, false);

            if (dstbset == null)
            {
                dstbset = await LoadBackupSet(new BackupSetReference(bsname, false, false, false));
            }
            if (cachebset == null)
            {
                cachebset = await cache.LoadBackupSet(new BackupSetReference(bsname, false, false, false));
            }
            if (cachebset.Backups.Count > 0 && dstbset.Backups.Count > 0)
            {
                while (cachebset.Backups.Count > cacheindex && dstbset.Backups.Count > dstindex)
                {
                    if (!cachebset.Backups[cacheindex].hash.SequenceEqual(dstbset.Backups[dstindex].hash))
                    {
                        if (cachebr == null)
                        {
                            cachebr = await cache.GetBackupRecord(cachebsname, cachebset.Backups[cacheindex].hash);
                        }
                        if (dstbr == null)
                        {
                            dstbr = await GetBackupRecord(new BackupSetReference(bsname, false, false, false), dstbset.Backups[dstindex].hash);
                        }
                        if (cachebr.BackupTime < dstbr.BackupTime)
                        {
                            if (cachebset.Backups[cacheindex].shallow)
                            {
                                // Remove shallow backups from cache not present in dst
                                await cache.Dependencies.Blobs.DecrementReferenceCount(cachebsname, cachebset.Backups[cacheindex].hash, 
                                    BlobLocation.BlobType.BackupRecord, false);
                                cachebset.Backups.RemoveAt(cacheindex);
                            }
                            else
                            {
                                // Add non shallow backups from cache not present in dst
                                dstbset.Backups.Insert(dstindex, (cachebset.Backups[cacheindex].hash, false));
                                await cache.Dependencies.Blobs.TransferBackup(Dependencies.Blobs, new BackupSetReference(bsname, false, false, false), cachebset.Backups[cacheindex].hash, true);

                                // After transfer, make the cache backup shallow
                                // Since no clean way to only get file references and not "parent" references,
                                // we delete the entire backup data from cache, then add it back shallow
                                // TODO: Means to iterate through blobs only including files
                                await cache.Dependencies.Blobs.DecrementReferenceCount(cachebsname, cachebset.Backups[cacheindex].hash,
                                    BlobLocation.BlobType.BackupRecord, true);
                                await Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, dstbset.Backups[dstindex].hash, false);
                                dstindex += 1;
                                // After insert and increment j still referes to the same backup (dstbr)
                                cacheindex += 1;
                            }
                            cachebr = null;
                        }
                        else // (srcbr.BackupTime > dstbr.BackupTime)
                        {
                            // Add (as shallow) backups in dst not present in cache
                            cachebset.Backups.Insert(cacheindex, (dstbset.Backups[dstindex].hash, true));
                            await Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, dstbset.Backups[dstindex].hash, false);
                            cacheindex += 1;
                            dstindex += 1;
                            dstbr = null;
                        }
                    }
                    else
                    {
                        cacheindex += 1;
                        cachebr = null;
                        dstindex += 1;
                        dstbr = null;
                    }
                }
            }
            // Handle backups "dangling" after merge
            while (cacheindex < cachebset.Backups.Count)
            {
                if (cachebset.Backups[cacheindex].shallow)
                {
                    // Remove shallow backups from cache not present in dst
                    await cache.Dependencies.Blobs.DecrementReferenceCount(cachebsname, cachebset.Backups[cacheindex].hash,
                        BlobLocation.BlobType.BackupRecord, false);
                    cachebset.Backups.RemoveAt(cacheindex);
                }
                else
                {
                    // Add non shallow backups from cache not present in dst
                    dstbset.Backups.Add((cachebset.Backups[cacheindex].hash, false));
                    await cache.Dependencies.Blobs.TransferBackup(Dependencies.Blobs, new BackupSetReference(bsname, false, false, false), cachebset.Backups[cacheindex].hash, true);
                    dstindex += 1;
                    // After insert and increment j still referes to the same backup (dstbr)
                    cacheindex += 1;
                }
            }
            while (dstindex < dstbset.Backups.Count)
            {
                // Add (as shallow) backups in dst not present in cache
                cachebset.Backups.Add((dstbset.Backups[dstindex].hash, true));
                await Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, dstbset.Backups[dstindex].hash, false);
                cacheindex += 1;
                dstindex += 1;
            }
            await Dependencies.Blobs.CacheBlobList(bsname, cache.Dependencies.Blobs);
            return (dstbset, cachebset);
        }

        public async IAsyncEnumerable<string> GetBackupsAndMetadataReferencesAsStrings(BackupSetReference bsname)
        {
            var bset = await LoadBackupSet(bsname);
            foreach ((byte[] backupref, bool _) in bset.Backups)
            {
                yield return HashTools.ByteArrayToHexViaLookup32(backupref);
                await foreach (byte[] reference in Dependencies.Blobs.GetAllBlobReferences(backupref, 
                    BlobLocation.BlobType.BackupRecord, false, false))
                {
                    yield return HashTools.ByteArrayToHexViaLookup32(reference);
                }
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bsname"></param>
        /// <param name="message"></param>
        /// <param name="metadatatreehash"></param>
        /// <param name="shallow"></param>
        /// <returns>The hash of the new backup</returns>
        public async Task<byte[]> AddBackup(BackupSetReference bsname, string message, byte[] metadatatreehash, DateTime backupTime, BackupSet? bset=null)
        {
            if (bset == null)
            {
                bset = await LoadBackupSet(bsname);
            }
            BackupRecord newbackup = new(message, metadatatreehash, backupTime);
            byte[] brbytes = newbackup.Serialize();
            byte[] backuphash = await BlobStore.StoreData(new List<BlobStore>(1) { Dependencies.Blobs }, bsname, brbytes);
            bset.Backups.Add((backuphash, bsname.Shallow));
            return backuphash;
        }

        public async Task RemoveBackup(BackupSetReference bsname, string backuphashprefix, bool dst_wo_cache, bool force_delete = false)
        {
            var bset = await LoadBackupSet(bsname);
            if (bset.CacheUsed && dst_wo_cache && !force_delete)
            {
                // TODO: Do we need this check in more places?
                throw new Core.BackupRemoveException("Deleting a backup from a backup destination that uses a cache, " +
                    "without that cache present may cause errors when merging cache.");
            }
            var match = await HashByPrefix(bsname, backuphashprefix);
            // TODO: Better error messages depending on return value of HashByPrefix()
            // TODO: Cleanup usage of strings vs byte[] for hashes between backup store and Core
            if (match == null || match.Value.multiplematches == true)
            {
                throw new KeyNotFoundException();
            }
            byte[] backuphash;
            if (match.Value.singlematchhash != null) 
            {
                backuphash = match.Value.singlematchhash;
            } 
            else
            {
                throw new Exception("HashByPrefix returned an incorrect type");
            }
            int i;
            for (i = 0; i < bset.Backups.Count; i++)
            {
                if (bset.Backups[i].hash.SequenceEqual(backuphash))
                {
                    break;
                }
            }
            await Dependencies.Blobs.DecrementReferenceCount(bsname, backuphash, BlobLocation.BlobType.BackupRecord, !bset.Backups[i].shallow);
            bset.Backups.RemoveAt(i);
            await SaveBackupSet(bset, bsname);
        }

        /// <summary>
        /// Gets the latest BackupRecord in this BackupStore.
        /// Returns null if no backups.
        /// </summary>
        /// <param name="bsname"></param>
        /// <returns></returns>
        public async Task<BackupRecord> GetBackupRecord(BackupSetReference bsname)
        {
            var bset = await LoadBackupSet(bsname);
            if (bset.Backups.Count > 0)
            {
                return await GetBackupRecord(bsname, bset.Backups[^1].hash);
            }
            throw new IndexOutOfRangeException("No backup records present in this backup set");
        }

        public async Task<BackupRecord> GetBackupRecord(BackupSetReference bsname, string? prefix)
        {
            if (prefix == null)
            {
                return await GetBackupRecord(bsname);
            }
            var match = await HashByPrefix(bsname, prefix);
            if (match == null || match.Value.multiplematches == true)
            {
                throw new KeyNotFoundException();
            }
            if (match.Value.singlematchhash != null)
            {
                return await GetBackupRecord(bsname, match.Value.singlematchhash);
            }
            else
            {
                throw new Exception("HashByPrefix returned an incorrect type");
            }
        }

        public async Task<BackupRecord> GetBackupRecord(BackupSetReference bsname, byte[] hash)
        {
            if (hash == null)
            {
                return await GetBackupRecord(bsname);
            }
            return BackupRecord.Deserialize(await Dependencies.Blobs.RetrieveData(hash));
        }

        public async Task<(string, BackupRecord)> GetBackupHashAndRecord(BackupSetReference bsname, int offset = 0)
        {
            var bset = await LoadBackupSet(bsname);
            return await GetBackupHashAndRecord(bsname, HashTools.ByteArrayToHexViaLookup32(bset.Backups[^1].hash).ToLower(), offset);
        }

        public async Task<(string, BackupRecord)> GetBackupHashAndRecord(BackupSetReference bsname, string prefix, int offset = 0)
        {
            var bset = await LoadBackupSet(bsname);
            var match = await HashByPrefix(bsname, prefix);
            if (match == null || match.Value.multiplematches == true)
            {
                // TODO: throw this exception out of HashByPrefix?
                throw new KeyNotFoundException();
            }
            int pidx = 0;
            for (int i = 0; i < bset.Backups.Count; i++)
            {
                if (bset.Backups[i].hash.SequenceEqual(match.Value.singlematchhash)) // TODO: Handle this possible null
                {
                    pidx = i;
                    break;
                }
            }
            int bidx = pidx + offset;
            if (bidx >= 0 && bidx < bset.Backups.Count)
            {
                return (HashTools.ByteArrayToHexViaLookup32(bset.Backups[bidx].hash).ToLower(), await GetBackupRecord(bsname, bset.Backups[bidx].hash));
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public async Task<List<BackupRecord>> GetAllBackupRecords(BackupSetReference bsname)
        {
            var bset = await LoadBackupSet(bsname);
            return (await Task.WhenAll(bset.Backups.Select(backup => GetBackupRecord(bsname, backup.hash)))).ToList();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns>Null if no matches, (true, null) for multiple matches, (false, hashstring) for exact match.</returns>
        public async Task<(bool multiplematches, byte[]? singlematchhash)?> HashByPrefix(BackupSetReference bsname, string prefix)
        {
            var bset = await LoadBackupSet(bsname);
            // TODO: This implementation is pretty slow, could be improved with a better data structure like a trie or DAFSA
            // also if this becomes an issue, keep a s
            prefix = prefix.ToLower();
            List<string> hashes = new(from backup in bset.Backups select HashTools.ByteArrayToHexViaLookup32(backup.hash));
            List<string> matches = new(from h in hashes where h.ToLower().StartsWith(prefix.ToLower()) select h);
            if (matches.Count == 0)
            {
                return null;
            }
            else if (matches.Count > 1)
            {
                return (true, null);
            }
            else
            {
                return (false, HashTools.HexStringToByteArray(matches[0]));
            }
        }

        /// <summary>
        /// Loads a BackupSet from a file.
        /// </summary>
        /// <param name="backuplistfile"></param>
        /// <param name="blobs"></param>
        /// <returns>A previously stored BackupStore object</returns>
        public async Task<BackupSet> LoadBackupSet(BackupSetReference bsname)
        {
            return BackupSet.Deserialize(await Dependencies.LoadBackupSetData(bsname));
        }

        /// <summary>
        /// Attempts to save the BackupStore to disk.
        /// If saving fails an error is thrown.
        /// </summary>
        /// <param name="path"></param>
        public async Task SaveBackupSet(BackupSet bset, BackupSetReference bsname)
        {
            // NOTE: This overwrites the previous file every time.
            // This should be okay as the serialized BackupStore filesize should always be small.
            await Dependencies.StoreBackupSetData(bsname, bset.Serialize());
        }
    }
}