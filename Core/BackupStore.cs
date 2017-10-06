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
        public string DiskStorePath { get; set; }

        private BlobStore Blobs { get; set; }

        public BackupStore(string savepath, BlobStore blobs)
        {
            DiskStorePath = savepath;
            Blobs = blobs;
        }

        public void SyncCache(BackupStore cache, string bsname)
        {
            int srcindex = 0;
            int dstindex = 0;
            BackupRecord srcbr = null;
            BackupRecord dstbr = null;
            var bset = LoadBackupSet(bsname);
            var chachebset = cache.LoadBackupSet(bsname);
            if (chachebset.Backups.Count > 0 && bset.Backups.Count > 0)
            {
                while (chachebset.Backups.Count > srcindex && bset.Backups.Count > dstindex)
                {
                    if (!chachebset.Backups[srcindex].Item1.SequenceEqual(bset.Backups[dstindex].Item1))
                    {
                        if (srcbr == null)
                        {
                            srcbr = cache.GetBackupRecord(bsname, chachebset.Backups[srcindex].Item1);
                        }
                        if (dstbr == null)
                        {
                            dstbr = cache.GetBackupRecord(bsname, bset.Backups[dstindex].Item1);
                        }
                        if (srcbr.BackupTime < dstbr.BackupTime)
                        {
                            if (chachebset.Backups[srcindex].Item2)
                            {
                                // Remove shallow backups from cache not present in dst
                                cache.Blobs.IncrementReferenceCount(bsname, chachebset.Backups[srcindex].Item1, -1, false);
                                chachebset.Backups.RemoveAt(srcindex);
                            }
                            else
                            {
                                // Add non shallow backups from cache not present in dst
                                bset.Backups.Insert(dstindex, new Tuple<byte[], bool>(chachebset.Backups[srcindex].Item1, false));
                                cache.Blobs.TransferBackup(Blobs, bsname, chachebset.Backups[srcindex].Item1, true);
                                dstindex += 1;
                                // After insert and increment j still referes to the same backup (dstbr)
                                srcindex += 1;
                            }
                            srcbr = null;
                        }
                        else // (srcbr.BackupTime > dstbr.BackupTime)
                        {
                            // Add (as shallow) backups in dst not present in cache
                            chachebset.Backups.Insert(srcindex, new Tuple<byte[], bool>(bset.Backups[dstindex].Item1, true));
                            Blobs.TransferBackup(cache.Blobs, bsname, bset.Backups[dstindex].Item1, false);
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
                if (chachebset.Backups[srcindex].Item2)
                {
                    // Remove shallow backups from cache not present in dst
                    cache.Blobs.IncrementReferenceCount(bsname, chachebset.Backups[srcindex].Item1, -1, false);
                    chachebset.Backups.RemoveAt(srcindex);
                }
                else
                {
                    // Add non shallow backups from cache not present in dst
                    bset.Backups.Add(new Tuple<byte[], bool>(chachebset.Backups[srcindex].Item1, false));
                    cache.Blobs.TransferBackup(Blobs, bsname, chachebset.Backups[srcindex].Item1, true);
                    dstindex += 1;
                    // After insert and increment j still referes to the same backup (dstbr)
                    srcindex += 1;
                }
                srcbr = null;
            }
            while (dstindex < bset.Backups.Count)
            {
                // Add (as shallow) backups in dst not present in cache
                chachebset.Backups.Add(new Tuple<byte[], bool>(bset.Backups[dstindex].Item1, true));
                Blobs.TransferBackup(cache.Blobs, bsname, bset.Backups[dstindex].Item1, false);
                srcindex += 1;
                dstindex += 1;
                dstbr = null;
            }
            cache.SaveBackupSet(chachebset, bsname);
            SaveBackupSet(bset, bsname);
        }

        public IEnumerable<string> GetBackupsAndMetadataReferencesAsStrings(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            foreach ((byte[] backupref, bool _) in bset.Backups)
            {
                yield return HashTools.ByteArrayToHexViaLookup32(backupref);
                foreach (byte[] reference in Blobs.GetAllBlobReferences(backupref, false))
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
            byte[] backuphash = Blobs.StoreDataSync(bsname, brbytes, BlobLocation.BlobTypes.BackupRecord);
            bset.Backups.Add(new Tuple<byte[], bool>(backuphash, shallow));
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
                if (bset.Backups[i].Item1.SequenceEqual(backuphash))
                {
                    bset.Backups.RemoveAt(i);
                }
            }
            SaveBackupSet(bset, bsname);
            Blobs.IncrementReferenceCount(bsname, backuphash, -1, !bset.Backups[i].Item2);
        }

        public BackupRecord GetBackupRecord(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            if (bset.Backups.Count > 0)
            {
                return GetBackupRecord(bsname, bset.Backups[bset.Backups.Count - 1].Item1);
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
            return BackupRecord.deserialize(Blobs.RetrieveData(hash));
        }

        public (string, BackupRecord) GetBackupHashAndRecord(string bsname, int offset = 0)
        {
            var bset = LoadBackupSet(bsname);
            return GetBackupHashAndRecord(bsname, HashTools.ByteArrayToHexViaLookup32(bset.Backups[bset.Backups.Count - 1].Item1).ToLower(), offset);
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
                if (bset.Backups[i].Item1.SequenceEqual(match.Value.singlematchhash))
                {
                    pidx = i;
                    break;
                }
            }
            int bidx = pidx + offset;
            if (bidx >= 0 && bidx < bset.Backups.Count)
            {
                return (HashTools.ByteArrayToHexViaLookup32(bset.Backups[bidx].Item1).ToLower(), GetBackupRecord(bsname, bset.Backups[bidx].Item1));
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public List<BackupRecord> GetAllBackupRecords(string bsname)
        {
            var bset = LoadBackupSet(bsname);
            return new List<BackupRecord>(from backup in bset.Backups select GetBackupRecord(bsname, backup.Item1));
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
            List<string> hashes = new List<string>(from backup in bset.Backups select HashTools.ByteArrayToHexViaLookup32(backup.Item1));
            List<string> matches = new List<string>(from h in hashes where h.Substring(0, prefix.Length).ToLower() == prefix.ToLower() select h);
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
            var backuplistfile = Path.Combine(DiskStorePath, bsname);
            try
            {
                using (FileStream fs = new FileStream(backuplistfile, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        return BackupSet.deserialize(reader.ReadBytes((int)fs.Length));
                    }
                }
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
        public void SaveBackupSet(BackupSet bset, string bsname, string path = null)
        {
            // NOTE: This overwrites the previous file every time.
            // This should be okay as the serialized BackupStore filesize should always be small.
            if (path == null)
            {
                path = DiskStorePath;
            }
            path = Path.Combine(path, bsname);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(bset.serialize());
                }
            }
        }
    }
}