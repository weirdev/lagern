using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class MetadataStore : ICustomSerializable<MetadataStore>
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

        public BackupRecord LatestBackup
        {
            get
            {
                if (backups.Count > 0)
                {
                    return backups[backups.Count - 1];
                }
                return null;
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
    }
}
