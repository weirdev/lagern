using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    class BackupStoreDependencies : IBackupStoreDependencies
    {
        public BlobStore Blobs { get; set; }

        private IDstFSInterop DstFSInterop { get; set; }

        public BackupStoreDependencies(IDstFSInterop cloudinterop, BlobStore blobs)
        {
            DstFSInterop = cloudinterop;
            Blobs = blobs;
        }

        public byte[] LoadBackupSetData(string backupsetname)
        {
            return DstFSInterop.LoadIndexFileAsync(backupsetname, IndexFileType.BackupSet).Result;
        }

        public void StoreBackupSetData(string backupsetname, byte[] bsdata)
        {
            DstFSInterop.StoreIndexFileAsync(backupsetname, IndexFileType.BackupSet, bsdata);
        }
    }
}
