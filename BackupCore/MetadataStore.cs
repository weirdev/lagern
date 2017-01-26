using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class MetadataStore : IList<BackupRecord>, ICustomSerializable<MetadataStore>
    {
        List<BackupRecord> backups;
        public MetadataStore(string metadatapath)
        {
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
                Console.WriteLine("Reading old metadata failed. Initializing new metadata store...");
                backups = new List<BackupRecord>();
            }
        }

        public void AddBackup(string message, List<byte[]> metadatatreehashes)
        {
            backups.Add(new BackupRecord(message, metadatatreehashes));
        }

        public int Count
        {
            get
            {
                return ((IList<BackupRecord>)backups).Count;
            }
        }

        public bool IsReadOnly
        {
            get
            {
                return ((IList<BackupRecord>)backups).IsReadOnly;
            }
        }

        public BackupRecord this[int index]
        {
            get
            {
                return ((IList<BackupRecord>)backups)[index];
            }

            set
            {
                ((IList<BackupRecord>)backups)[index] = value;
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

        public byte[] serialize()
        {
            List<byte> binrep = new List<byte>();
            foreach (BackupRecord br in backups)
            {
                BinaryEncoding.encode(br.serialize(), binrep);
            }

            return binrep.ToArray();
        }

        private List<BackupRecord> deserialize(byte[] data)
        {
            List<byte[]> savedobjects = BinaryEncoding.decode(data);

            List<BackupRecord> savedbackups = new List<BackupRecord>();
            foreach (var backup in savedobjects)
            {
                savedbackups.Add(BackupRecord.deserialize(backup));
            }
            return savedbackups;
        }

        public int IndexOf(BackupRecord item)
        {
            return ((IList<BackupRecord>)backups).IndexOf(item);
        }

        public void Insert(int index, BackupRecord item)
        {
            ((IList<BackupRecord>)backups).Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            ((IList<BackupRecord>)backups).RemoveAt(index);
        }

        public void Add(BackupRecord item)
        {
            ((IList<BackupRecord>)backups).Add(item);
        }

        public void Clear()
        {
            ((IList<BackupRecord>)backups).Clear();
        }

        public bool Contains(BackupRecord item)
        {
            return ((IList<BackupRecord>)backups).Contains(item);
        }

        public void CopyTo(BackupRecord[] array, int arrayIndex)
        {
            ((IList<BackupRecord>)backups).CopyTo(array, arrayIndex);
        }

        public bool Remove(BackupRecord item)
        {
            return ((IList<BackupRecord>)backups).Remove(item);
        }

        public IEnumerator<BackupRecord> GetEnumerator()
        {
            return ((IList<BackupRecord>)backups).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IList<BackupRecord>)backups).GetEnumerator();
        }
    }
}
