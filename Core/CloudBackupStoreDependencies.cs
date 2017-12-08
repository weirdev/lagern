using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    class CloudBackupStoreDependencies : IBackupStoreDependencies
    {
        public BlobStore Blobs { get; set; }

        private ICloudInterop CloudInterop { get; set; }

        public CloudBackupStoreDependencies(ICloudInterop cloudinterop, BlobStore blobs)
        {
            CloudInterop = cloudinterop;
            Blobs = blobs;
        }

        public byte[] LoadBackupSetData(string backupsetname)
        {
            return CloudInterop.DownloadFileAsync(backupsetname).Result;
        }

        public async void StoreBackupSetData(string backupsetname, byte[] bsdata)
        {
            await CloudInterop.UploadFileAsync(backupsetname, bsdata);
        }
    }
}
