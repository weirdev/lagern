using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BackupStore : IEnumerable<BackupRecord>, ICustomSerializable<BackupStore>
    {
        List<BackupRecord> backups;
        Dictionary<string, int> backupidx = new Dictionary<string, int>();
        private Core BCore { get; set; }
        public BackupStore(string metadatapath, Core core)
        {
            BCore = core;
            try
            {
                using (FileStream fs = new FileStream(metadatapath, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        backups = deserialize(reader.ReadBytes((int)fs.Length));
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Reading old backup failed. Initializing new backup store...");
                backups = new List<BackupRecord>();
            }
            for (int i = 0; i < backups.Count; i++)
            {
                string hashhex = HashTools.ByteArrayToHexViaLookup32(backups[i].MetadataTreeHash).ToLower();
                if (!backupidx.ContainsKey(hashhex)) // We dont care about duplicates, just as long as we point to one of them
                {
                    backupidx.Add(hashhex, i);
                }
            }
        }

        public void AddBackup(string message, byte[] metadatatreehash)
        {
            BackupRecord newbackup = new BackupRecord(message, metadatatreehash);
            
            backups.Add(newbackup);
            string hashstring = HashTools.ByteArrayToHexViaLookup32(newbackup.MetadataTreeHash).ToLower();
            if (!backupidx.ContainsKey(hashstring))
            {
                backupidx.Add(hashstring, backups.Count - 1);
            }
        }

        public void Remove(string backuphash)
        {
            Tuple<bool, string> match = HashByPrefix(backuphash);
            if (match == null || match.Item1 == true)
            {
                throw new KeyNotFoundException();
            }
            backuphash = match.Item2;
            BCore.RemoveBackup(backuphash);
        }

        public int Count
        {
            get
            {
                return ((IList<BackupRecord>)backups).Count;
            }
        }

        public ICollection<string> Keys
        {
            get
            {
                return backupidx.Keys;
            }
        }

        public ICollection<BackupRecord> Values
        {
            get
            {
                return backups;
            }
        }

        public BackupRecord this[string key]
        {
            get
            {
                if (key == null)
                {
                    key = HashTools.ByteArrayToHexViaLookup32(this[this.Count - 1].MetadataTreeHash);
                }
                else
                {
                    Tuple<bool, string> match = HashByPrefix(key);
                    // TODO: Better error messages depending on return value of HashByPrefix()
                    if (match == null || match.Item1 == true)
                    {
                        throw new KeyNotFoundException();
                    }
                    key = match.Item2;
                }
                key = key.ToLower();
                return backups[backupidx[key]];
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="prefix"></param>
        /// <returns>Null if no matches, (true, null) for multiple matches, (false, hashstring) for exact match.</returns>
        public Tuple<bool, string> HashByPrefix(string prefix)
        {
            // TODO: This implementation is pretty slow, could be improved with a better data structure like a trie or DAFSA
            // also if this becomes an issue, keep a s
            prefix = prefix.ToLower();
            List<string> hashes = new List<string>(backupidx.Keys);
            List<string> matches = new List<string>(from h in hashes where h.Substring(0, prefix.Length).ToLower() == prefix.ToLower() select h);
            if (matches.Count == 0)
            {
                return null;
            }
            else if (matches.Count > 1)
            {
                return new Tuple<bool, string>(true, null);
            }
            else
            {
                return new Tuple<bool, string>(false, matches[0].ToLower());
            }
        }

        public BackupRecord this[int index]
        {
            get
            {
                return ((IList<BackupRecord>)backups)[index];
            }
        }

        public void SynchronizeCacheToDisk(string path)
        {

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(this.serialize());
                }
            }
        }

        public int IndexOf(BackupRecord item)
        {
            return ((IList<BackupRecord>)backups).IndexOf(item);
        }

        public bool Contains(BackupRecord item)
        {
            return ((IList<BackupRecord>)backups).Contains(item);
        }

        public IEnumerator<BackupRecord> GetEnumerator()
        {
            return ((IList<BackupRecord>)backups).GetEnumerator();
        }

        public bool ContainsKey(string key)
        {
            return backupidx.ContainsKey(key);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<BackupRecord>)backups).GetEnumerator();
        }

        public byte[] serialize()
        {
            return BinaryEncoding.enum_encode(from b in backups select b.serialize());
        }

        private List<BackupRecord> deserialize(byte[] data)
        {
            return new List<BackupRecord>(from bin in BinaryEncoding.enum_decode(data) select BackupRecord.deserialize(bin));
        }
    }
}