using System;
using System.IO;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public class FSBackupStoreDependencies : IBackupStoreDependencies
    {
        public BlobStore Blobs { get; set; }

        private string DiskStorePath { get; set; }

        public FSBackupStoreDependencies(BlobStore blobs, string savepath)
        {
            Blobs = blobs;
            DiskStorePath = savepath;
        }

        public byte[] LoadBackupSetData(string backupsetname)
        {
            var backuplistfile = Path.Combine(DiskStorePath, backupsetname);
            try
            {
                using (FileStream fs = new FileStream(backuplistfile, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        return reader.ReadBytes((int)fs.Length);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public void StoreBackupSetData(string backupsetname, byte[] bsdata)
        {
            // NOTE: This overwrites the previous file every time.
            // This should be okay as the serialized BackupStore filesize should always be small
            string path = Path.Combine(DiskStorePath, backupsetname);
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(bsdata);
                }
            }
        }
    }
}
