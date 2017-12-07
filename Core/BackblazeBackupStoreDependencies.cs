using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    class BackblazeBackupStoreDependencies : IBackupStoreDependencies
    {
        public BlobStore Blobs { get; set; }

        private BackblazeInterop BBInterop { get; set; }

        public BackblazeBackupStoreDependencies(BackblazeInterop bbinterop, BlobStore blobs)
        {
            BBInterop = bbinterop;
            Blobs = blobs;
        }

        public byte[] LoadBackupSetData(string backupsetname)
        {
            return BBInterop.DownloadFile(backupsetname).Result;
        }

        public async void StoreBackupSetData(string backupsetname, byte[] bsdata)
        {
            await BBInterop.UploadFileAsync(backupsetname, bsdata);
        }
    }
}
