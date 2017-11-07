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

        private IFSInterop FSInterop { get; set; }

        public FSBackupStoreDependencies(IFSInterop fsinterop, BlobStore blobs, string savepath)
        {
            Blobs = blobs;
            DiskStorePath = savepath;
            FSInterop = fsinterop;
        }

        public byte[] LoadBackupSetData(string backupsetname)
        {
            var backuplistfile = Path.Combine(DiskStorePath, backupsetname);
            try
            {
                return FSInterop.ReadAllFileBytes(backuplistfile);
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
            FSInterop.OverwriteOrCreateFile(path, bsdata);
        }
    }
}
