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

        public void SyncCache(BackupStore cache, string bsname)
        {
            int srcindex = 0;
            int dstindex = 0;
            BackupRecord srcbr = null;
            BackupRecord dstbr = null;

            string cachebsname = bsname + Core.CacheSuffix;
            
            var bset = LoadBackupSet(bsname);
            var chachebset = cache.LoadBackupSet(cachebsname);
            if (chachebset.Backups.Count > 0 && bset.Backups.Count > 0)
            {
                while (chachebset.Backups.Count > srcindex && bset.Backups.Count > dstindex)
                {
                    if (!chachebset.Backups[srcindex].hash.SequenceEqual(bset.Backups[dstindex].hash))
                    {
                        if (srcbr == null)
                        {
                            srcbr = cache.GetBackupRecord(cachebsname, chachebset.Backups[srcindex].hash);
                        }
                        if (dstbr == null)
                        {
                            dstbr = cache.GetBackupRecord(cachebsname, bset.Backups[dstindex].hash);
                        }
                        if (srcbr.BackupTime < dstbr.BackupTime)
                        {
                            if (chachebset.Backups[srcindex].shallow)
                            {
                                // Remove shallow backups from cache not present in dst
                                cache.Dependencies.Blobs.IncrementReferenceCount(cachebsname, chachebset.Backups[srcindex].hash, -1, false);
                                chachebset.Backups.RemoveAt(srcindex);
                            }
                            else
                            {
                                // Add non shallow backups from cache not present in dst
                                bset.Backups.Insert(dstindex, (chachebset.Backups[srcindex].hash, false));
                                cache.Dependencies.Blobs.TransferBackup(Dependencies.Blobs, bsname, chachebset.Backups[srcindex].hash, true);

                                // After transfer, make the cache backup shallow
                                // Since no clean way to only get file references and not "parent" references,
                                // we delete the entire backup data from cache, then add it back shallow
                                // TODO: Means to iterate through blobs not including files
                                cache.Dependencies.Blobs.IncrementReferenceCount(cachebsname, chachebset.Backups[srcindex].hash, -1, true);
                                Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, bset.Backups[dstindex].hash, false);
                                dstindex += 1;
                                // After insert and increment j still referes to the same backup (dstbr)
                                srcindex += 1;
                            }
                            srcbr = null;
                        }
                        else // (srcbr.BackupTime > dstbr.BackupTime)
                        {
                            // Add (as shallow) backups in dst not present in cache
                            chachebset.Backups.Insert(srcindex, (bset.Backups[dstindex].hash, true));
                            Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, bset.Backups[dstindex].hash, false);
                            srcindex += 1;
                            dstindex += 1;
                            dstbr = null;
                        }
                    }
                    else
                    {
                        srcindex += 1;
                        srcbr = null;
                        dstindex += 1;
                        dstbr = null;
                    }
                }
            }
            // Handle backups "dangling" after merge
            while (srcindex < chachebset.Backups.Count)
            {
                if (chachebset.Backups[srcindex].shallow)
                {
                    // Remove shallow backups from cache not present in dst
                    cache.Dependencies.Blobs.IncrementReferenceCount(cachebsname, chachebset.Backups[srcindex].hash, -1, false);
                    chachebset.Backups.RemoveAt(srcindex);
                }
                else
                {
                    // Add non shallow backups from cache not present in dst
                    bset.Backups.Add((chachebset.Backups[srcindex].hash, false));
                    cache.Dependencies.Blobs.TransferBackup(Dependencies.Blobs, bsname, chachebset.Backups[srcindex].hash, true);
                    dstindex += 1;
                    // After insert and increment j still referes to the same backup (dstbr)
                    srcindex += 1;
                }
                srcbr = null;
            }
            while (dstindex < bset.Backups.Count)
            {
                // Add (as shallow) backups in dst not present in cache
                chachebset.Backups.Add((bset.Backups[dstindex].hash, true));
                Dependencies.Blobs.TransferBackup(cache.Dependencies.Blobs, cachebsname, bset.Backups[dstindex].hash, false);
                srcindex += 1;
                dstindex += 1;
                dstbr = null;
            }
            cache.SaveBackupSet(chachebset, cachebsname);
            SaveBackupSet(bset, bsname);
            Dependencies.Blobs.CacheBlobList(bsname, cache.Dependencies.Blobs);
        }


        public IEnumerable<string> GetBackupsAndMetadataReferencesAsStrings(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            foreach ((byte[] backupref, bool _) in bset.Backups)
            {
                yield return HashTools.ByteArrayToHexViaLookup32(backupref);
                foreach (byte[] reference in Dependencies.Blobs.GetAllBlobReferences(backupref, false))
                {
                    yield return HashTools.ByteArrayToHexViaLookup32(reference);
                }
            }
        }

        public void AddBackup(string bsname, string message, byte[] metadatatreehash, bool shallow)
        {
            var bset = LoadBackupSet(bsname);
            BackupRecord newbackup = new BackupRecord(message, metadatatreehash);
            byte[] brbytes = newbackup.serialize();
            byte[] backuphash = Dependencies.Blobs.StoreData(bsname, brbytes, BlobLocation.BlobTypes.BackupRecord);
            bset.Backups.Add((backuphash, shallow));
            SaveBackupSet(bset, bsname);
        }

        public void RemoveBackup(string bsname, string backuphashprefix)
        {
            var bset = LoadBackupSet(bsname);
            var match = HashByPrefix(bsname, backuphashprefix);
            // TODO: Better error messages depending on return value of HashByPrefix()
            // TODO: Cleanup usage of strings vs byte[] for hashes between backup store and Core
            if (match == null || match.Value.multiplematches == true)
            {
                throw new KeyNotFoundException();
            }
            byte[] backuphash = match.Value.singlematchhash;
            int i;
            for (i = 0; i < bset.Backups.Count; i++)
            {
                if (bset.Backups[i].hash.SequenceEqual(backuphash))
                {
                    break;
                }
            }
            Dependencies.Blobs.IncrementReferenceCount(bsname, backuphash, -1, !bset.Backups[i].shallow);
            bset.Backups.RemoveAt(i);
            SaveBackupSet(bset, bsname);
        }

        public BackupRecord GetBackupRecord(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            if (bset.Backups.Count > 0)
            {
                return GetBackupRecord(bsname, bset.Backups[bset.Backups.Count - 1].hash);
            }
            return null;
        }

        public BackupRecord GetBackupRecord(string bsname, string prefix)
        {
            if (prefix == null)
            {
                return GetBackupRecord(bsname);
            }
            var match = HashByPrefix(bsname, prefix);
            if (match == null || match.Value.multiplematches == true)
            {
                throw new KeyNotFoundException();
            }
            return GetBackupRecord(bsname, match.Value.singlematchhash);
        }

        public BackupRecord GetBackupRecord(string bsname, byte[] hash)
        {
            if (hash == null)
            {
                return GetBackupRecord(bsname);
            }
            return BackupRecord.deserialize(Dependencies.Blobs.RetrieveData(hash));
        }

        public (string, BackupRecord) GetBackupHashAndRecord(string bsname, int offset = 0)
        {
            var bset = LoadBackupSet(bsname);
            return GetBackupHashAndRecord(bsname, HashTools.ByteArrayToHexViaLookup32(bset.Backups[bset.Backups.Count - 1].hash).ToLower(), offset);
        }

        public (string, BackupRecord) GetBackupHashAndRecord(string bsname, string prefix, int offset = 0)
        {
            var bset = LoadBackupSet(bsname);
            var match = HashByPrefix(bsname, prefix);
            if (match == null || match.Value.multiplematches == true)
            {
                // TODO: throw this exception out of HashByPrefix?
                throw new KeyNotFoundException();
            }
            int pidx = 0;
            for (int i = 0; i < bset.Backups.Count; i++)
            {
                if (bset.Backups[i].hash.SequenceEqual(match.Value.singlematchhash))
                {
                    pidx = i;
                    break;
                }
            }
            int bidx = pidx + offset;
            if (bidx >= 0 && bidx < bset.Backups.Count)
            {
                return (HashTools.ByteArrayToHexViaLookup32(bset.Backups[bidx].hash).ToLower(), GetBackupRecord(bsname, bset.Backups[bidx].hash));
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public List<BackupRecord> GetAllBackupRecords(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            return new List<BackupRecord>(from backup in bset.Backups select GetBackupRecord(bsname, backup.hash));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns>Null if no matches, (true, null) for multiple matches, (false, hashstring) for exact match.</returns>
        public (bool multiplematches, byte[] singlematchhash)? HashByPrefix(string bsname, string prefix)
        {
            var bset = LoadBackupSet(bsname);
            // TODO: This implementation is pretty slow, could be improved with a better data structure like a trie or DAFSA
            // also if this becomes an issue, keep a s
            prefix = prefix.ToLower();
            List<string> hashes = new List<string>(from backup in bset.Backups select HashTools.ByteArrayToHexViaLookup32(backup.hash));
            List<string> matches = new List<string>(from h in hashes where h.ToLower().StartsWith(prefix.ToLower()) select h);
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
        /// Attempts to load a BackupSet from a file.
        /// If loading fails creates a new backupset.
        /// </summary>
        /// <param name="backuplistfile"></param>
        /// <param name="blobs"></param>
        /// <returns>A previously stored BackupStore object</returns>
        public BackupSet LoadBackupSet(string bsname)
        {
            try
            {
                return BackupSet.deserialize(Dependencies.LoadBackupSetData(bsname));
            }
            catch
            {
                return new BackupSet();
            }
        }

        /// <summary>
        /// Attempts to save the BackupStore to disk.
        /// If saving fails an error is thrown.
        /// </summary>
        /// <param name="path"></param>
        public void SaveBackupSet(BackupSet bset, string bsname)
        {
            // NOTE: This overwrites the previous file every time.
            // This should be okay as the serialized BackupStore filesize should always be small.
            Dependencies.StoreBackupSetData(bsname, bset.serialize());
        }
    }
}