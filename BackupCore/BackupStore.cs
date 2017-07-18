using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BackupStore : ICustomSerializable<BackupStore>
    {
        // Backuphash is reference in blobs to BackupRecord
        // Backup is shallow if only metadata is stored for that backup
        public List<Tuple<byte[], bool>> Backups { get; private set; }
        private BlobStore Blobs { get; set; }
        public string DiskStorePath { get; set; }

        public BackupStore(string savepath, BlobStore blobs)
        {
            DiskStorePath = savepath;
            Blobs = blobs;
            Backups = new List<Tuple<byte[], bool>>();
        }

        private BackupStore(string savepath, BlobStore blobs, List<Tuple<byte[], bool>> backups)
        {
            DiskStorePath = savepath;
            Blobs = blobs;
            Backups = backups;
        }

        public void SyncCache(BackupStore cache)
        {
            int srcindex = 0;
            int dstindex = 0;
            BackupRecord srcbr = null;
            BackupRecord dstbr = null;
            if (cache.Backups.Count > 0 && Backups.Count > 0)
            {
                while (cache.Backups.Count > srcindex && Backups.Count > dstindex)
                {
                    if (!cache.Backups[srcindex].Item1.SequenceEqual(Backups[dstindex].Item1))
                    {
                        if (srcbr == null)
                        {
                            srcbr = cache.GetBackupRecord(cache.Backups[srcindex].Item1);
                        }
                        if (dstbr == null)
                        {
                            dstbr = cache.GetBackupRecord(Backups[dstindex].Item1);
                        }
                        if (srcbr.BackupTime < dstbr.BackupTime)
                        {
                            if (cache.Backups[srcindex].Item2)
                            {
                                // Remove shallow backups from cache not present in dst
                                cache.Blobs.IncrementReferenceCount(cache.Backups[srcindex].Item1, -1, false);
                                cache.Backups.RemoveAt(srcindex);
                            }
                            else
                            {
                                // Add non shallow backups from cache not present in dst
                                Backups.Insert(dstindex, new Tuple<byte[], bool>(cache.Backups[srcindex].Item1, false));
                                cache.Blobs.TransferBackup(Blobs, cache.Backups[srcindex].Item1, true);
                                dstindex += 1;
                                // After insert and increment j still referes to the same backup (dstbr)
                                srcindex += 1;
                            }
                            srcbr = null;
                        }
                        else // (srcbr.BackupTime > dstbr.BackupTime)
                        {
                            // Add (as shallow) backups in dst not present in cache
                            cache.Backups.Insert(srcindex, new Tuple<byte[], bool>(Backups[dstindex].Item1, true));
                            Blobs.TransferBackup(cache.Blobs, Backups[dstindex].Item1, false);
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
            while (srcindex < cache.Backups.Count)
            {
                if (cache.Backups[srcindex].Item2)
                {
                    // Remove shallow backups from cache not present in dst
                    cache.Blobs.IncrementReferenceCount(cache.Backups[srcindex].Item1, -1, false);
                    cache.Backups.RemoveAt(srcindex);
                }
                else
                {
                    // Add non shallow backups from cache not present in dst
                    Backups.Add(new Tuple<byte[], bool>(cache.Backups[srcindex].Item1, false));
                    cache.Blobs.TransferBackup(Blobs, cache.Backups[srcindex].Item1, true);
                    dstindex += 1;
                    // After insert and increment j still referes to the same backup (dstbr)
                    srcindex += 1;
                }
                srcbr = null;
            }
            while (dstindex < Backups.Count)
            {
                // Add (as shallow) backups in dst not present in cache
                cache.Backups.Add(new Tuple<byte[], bool>(Backups[dstindex].Item1, true));
                Blobs.TransferBackup(cache.Blobs, Backups[dstindex].Item1, false);
                srcindex += 1;
                dstindex += 1;
                dstbr = null;
            }
        }

        public IEnumerable<string> GetBackupsAndMetadataReferencesAsStrings()
        {
            foreach ((byte[] backupref, bool _) in Backups)
            {
                yield return HashTools.ByteArrayToHexViaLookup32(backupref);
                foreach (byte[] reference in Blobs.GetAllBlobReferences(backupref, false))
                {
                    yield return HashTools.ByteArrayToHexViaLookup32(reference);
                }
            }
        }
        
        /// <summary>
        /// Attempts to load a BackupStore from a file.
        /// If loading fails an error is thrown.
        /// </summary>
        /// <param name="backuplistfile"></param>
        /// <param name="blobs"></param>
        /// <returns>A previously stored BackupStore object</returns>
        public static BackupStore LoadFromFile(string backuplistfile, BlobStore blobs)
        {
            using (FileStream fs = new FileStream(backuplistfile, FileMode.Open, FileAccess.Read))
            {
                using (BinaryReader reader = new BinaryReader(fs))
                {
                    return BackupStore.deserialize(reader.ReadBytes((int)fs.Length), backuplistfile, blobs);
                }
            }
        }

        public void AddBackup(string message, byte[] metadatatreehash, bool shallow)
        {
            BackupRecord newbackup = new BackupRecord(message, metadatatreehash);
            byte[] brbytes = newbackup.serialize();
            byte[] backuphash = Blobs.StoreDataSync(brbytes, BlobLocation.BlobTypes.BackupRecord);
            Backups.Add(new Tuple<byte[], bool>(backuphash, shallow));
        }

        public void RemoveBackup(string backuphashprefix)
        {
            Tuple<bool, byte[]> match = HashByPrefix(backuphashprefix);
            // TODO: Better error messages depending on return value of HashByPrefix()
            // TODO: Cleanup usage of strings vs byte[] for hashes between backup store and Core
            if (match == null || match.Item1 == true)
            {
                throw new KeyNotFoundException();
            }
            byte[] backuphash = match.Item2;
            int i;
            for (i = 0; i < Backups.Count; i++)
            {
                if (Backups[i].Item1.SequenceEqual(backuphash))
                {
                    Backups.RemoveAt(i);
                }
            }
            try
            {
                SynchronizeCacheToDisk();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
            Blobs.IncrementReferenceCount(backuphash, -1, !Backups[i].Item2);
            try
            {
                Blobs.SaveToDisk();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        public BackupRecord GetBackupRecord()
        {
            if (Backups.Count > 0)
            {
                return GetBackupRecord(Backups[Backups.Count - 1].Item1);
            }
            return null;
        }

        public BackupRecord GetBackupRecord(string prefix)
        {
            if (prefix == null)
            {
                return GetBackupRecord();
            }
            var match = HashByPrefix(prefix);
            if (match == null || match.Item1 == true)
            {
                throw new KeyNotFoundException();
            }
            return GetBackupRecord(match.Item2);
        }

        public BackupRecord GetBackupRecord(byte[] hash)
        {
            if (hash == null)
            {
                return GetBackupRecord();
            }
            return BackupRecord.deserialize(Blobs.RetrieveData(hash));
        }

        public Tuple<string, BackupRecord> GetBackupHashAndRecord(int offset = 0)
        {
            return GetBackupHashAndRecord(HashTools.ByteArrayToHexViaLookup32(Backups[Backups.Count - 1].Item1).ToLower(), offset);
        }

        public Tuple<string, BackupRecord> GetBackupHashAndRecord(string prefix, int offset=0)
        {
            var match = HashByPrefix(prefix);
            if (match == null || match.Item1 == true)
            {
                // TODO: throw this exception out of HashByPrefix?
                throw new KeyNotFoundException();
            }
            int pidx = 0;
            for (int i = 0; i < Backups.Count; i++)
            {
                if (Backups[i].Item1.SequenceEqual(match.Item2))
                {
                    pidx = i;
                    break;
                }
            }
            int bidx = pidx + offset;
            if (bidx >= 0 && bidx < Backups.Count)
            {
                return new Tuple<string, BackupRecord>(HashTools.ByteArrayToHexViaLookup32(Backups[bidx].Item1).ToLower(), GetBackupRecord(Backups[bidx].Item1));
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public List<BackupRecord> GetAllBackupRecords()
        {
            return new List<BackupRecord>(from backup in Backups select GetBackupRecord(backup.Item1));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns>Null if no matches, (true, null) for multiple matches, (false, hashstring) for exact match.</returns>
        public Tuple<bool, byte[]> HashByPrefix(string prefix)
        {
            // TODO: This implementation is pretty slow, could be improved with a better data structure like a trie or DAFSA
            // also if this becomes an issue, keep a s
            prefix = prefix.ToLower();
            List<string> hashes = new List<string>(from backup in Backups select HashTools.ByteArrayToHexViaLookup32(backup.Item1));
            List<string> matches = new List<string>(from h in hashes where h.Substring(0, prefix.Length).ToLower() == prefix.ToLower() select h);
            if (matches.Count == 0)
            {
                return null;
            }
            else if (matches.Count > 1)
            {
                return new Tuple<bool, byte[]>(true, null);
            }
            else
            {
                return new Tuple<bool, byte[]>(false, HashTools.HexStringToByteArray(matches[0]));
            }
        }

        /// <summary>
        /// Attempts to save the BackupStore to disk.
        /// If saving fails an error is thrown.
        /// </summary>
        /// <param name="path"></param>
        public void SynchronizeCacheToDisk(string path=null)
        {
            // NOTE: This overwrites the previous file every time.
            // This should be okay as the serialized BackupStore filesize should always be small.
            if (path == null)
            {
                path = DiskStorePath;
            }
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(this.serialize());
                }
            }
        }

        public byte[] serialize()
        {
            // -"-v1"
            // backuphashes = enum_encode([Backups.backuphash,...])
            // shallowflags = enum_encode([BitConverter.GetBytes(Bakups.shallow),...])
            byte[] backuphashes = BinaryEncoding.enum_encode(from backup in Backups select backup.Item1);
            byte[] shallowflags = BinaryEncoding.enum_encode(from backup in Backups select BitConverter.GetBytes(backup.Item2));
            return BinaryEncoding.dict_encode( new Dictionary<string, byte[]>
            {
                { "backuphashes-v1", backuphashes },
                { "shallowflags-v1", shallowflags }
            });
        }

        public static BackupStore deserialize(byte[] data, string metadatapath, BlobStore blobs)
        {
            Dictionary<string, byte[]> saved_objects = BinaryEncoding.dict_decode(data);
            List<byte[]> backuphashes = BinaryEncoding.enum_decode(saved_objects["backuphashes-v1"]);
            List<bool> shallowflags = new List<bool>(from bb in BinaryEncoding.enum_decode(saved_objects["shallowflags-v1"]) select BitConverter.ToBoolean(bb, 0));
            List<Tuple<byte[], bool>> backups = new List<Tuple<byte[], bool>>();
            for (int i = 0; i < backuphashes.Count; i++)
            {
                backups.Add(new Tuple<byte[], bool>(backuphashes[i], shallowflags[i]));
            }
            return new BackupStore(metadatapath, blobs, backups);
        }
    }
}