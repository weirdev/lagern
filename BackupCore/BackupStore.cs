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
        public List<byte[]> BackupHashes { get; set; }
        private BlobStore Blobs { get; set; }
        public string DiskStorePath { get; set; }

        public BackupStore(string metadatapath, BlobStore blobs)
        {
            DiskStorePath = metadatapath;
            Blobs = blobs;
        }

        private BackupStore(string metadatapath, BlobStore blobs, List<byte[]> backuphashes)
        {
            DiskStorePath = metadatapath;
            Blobs = blobs;
            BackupHashes = backuphashes;
        }

        public void AddBackup(string message, byte[] metadatatreehash)
        {
            BackupRecord newbackup = new BackupRecord(message, metadatatreehash);
            byte[] brbytes = newbackup.serialize();
            byte[] backuphash = Blobs.StoreDataSync(brbytes, BlobLocation.BlobTypes.BackupRecord);
            BackupHashes.Add(backuphash);

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
            for (int i = 0; i < BackupHashes.Count; i++)
            {
                if (BackupHashes[i].SequenceEqual(backuphash))
                {
                    BackupHashes.RemoveAt(i);
                }
            }
            SynchronizeCacheToDisk();
            Blobs.DereferenceOneDegree(backuphash);
            Blobs.SynchronizeCacheToDisk();
        }

        public BackupRecord GetBackupRecord()
        {
            if (BackupHashes.Count > 0)
            {
                return GetBackupRecord(BackupHashes[BackupHashes.Count - 1]);
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
            return BackupRecord.deserialize(Blobs.GetBlob(hash));
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
            for (int i = 0; i < BackupHashes.Count; i++)
            {
                if (BackupHashes[i].SequenceEqual(match.Item2))
                {
                    pidx = i;
                    break;
                }
            }
            int bidx = pidx + offset;
            if (bidx >= 0 && bidx < BackupHashes.Count)
            {
                return new Tuple<string, BackupRecord>(HashTools.ByteArrayToHexViaLookup32(BackupHashes[bidx]).ToLower(), GetBackupRecord(BackupHashes[bidx]));
            }
            else
            {
                throw new IndexOutOfRangeException();
            }
        }

        public Tuple<string, BackupRecord> GetFirstBackupHashAndRecord()
        {
            return new Tuple<string, BackupRecord>(HashTools.ByteArrayToHexViaLookup32(BackupHashes[BackupHashes.Count - 1]).ToLower(), GetBackupRecord(BackupHashes[BackupHashes.Count - 1]));
        }

        public List<BackupRecord> GetAllBackupRecords()
        {
            return new List<BackupRecord>(from hash in BackupHashes select GetBackupRecord(hash));
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
            List<string> hashes = new List<string>(from hash in BackupHashes select HashTools.ByteArrayToHexViaLookup32(hash));
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

        public void SynchronizeCacheToDisk(string path=null)
        {
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
            return BinaryEncoding.enum_encode(BackupHashes);
        }

        public static BackupStore deserialize(byte[] data, string metadatapath, BlobStore blobs)
        {
            return new BackupStore(metadatapath, blobs, new List<byte[]>(BinaryEncoding.enum_decode(data)));
        }
    }
}