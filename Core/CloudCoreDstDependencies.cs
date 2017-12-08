using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BackupCore
{
    public class CloudCoreDstDependencies : ICoreDstDependencies
    {
        public BlobStore Blobs { get; set; }
        public BackupStore Backups { get; set; }

        private ICloudInterop CloudInterop { get; set; }

        private static readonly string BackupBlobIndexFile = "hashindex";
        private static readonly string SettingsFilename = ".settings";

        private CloudCoreDstDependencies(ICloudInterop cloudinterop)
        {
            CloudInterop = cloudinterop;
        }

        public static CloudCoreDstDependencies InitializeNew(string bsname, ICloudInterop cloudinterop, bool cacheused)
        {
            CloudCoreDstDependencies clouddeps = new CloudCoreDstDependencies(cloudinterop);

            if (clouddeps.CloudInterop.FileExistsAsync(bsname).Result)
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!clouddeps.CloudInterop.FileExistsAsync(BackupBlobIndexFile).Result)
            {
                CloudBlobStoreDependencies blobStoreDependencies = new CloudBlobStoreDependencies(clouddeps.CloudInterop);
                clouddeps.Blobs = new BlobStore(blobStoreDependencies);
                clouddeps.SaveBlobStoreIndex();
            }
            else
            {
                throw new Exception();
            }
            CloudBackupStoreDependencies backupStoreDependencies = new CloudBackupStoreDependencies(clouddeps.CloudInterop, clouddeps.Blobs);
            clouddeps.Backups = new BackupStore(backupStoreDependencies);
            clouddeps.Backups.SaveBackupSet(new BackupSet(cacheused), bsname);
            return clouddeps;
        }

        public static CloudCoreDstDependencies Load(ICloudInterop cloudinterop, bool cacheused = false)
        {
            CloudCoreDstDependencies clouddeps = new CloudCoreDstDependencies(cloudinterop);
            // Would possibly load a cached blobindex file here
            (clouddeps.Blobs, clouddeps.Backups) = clouddeps.LoadIndex();
            return clouddeps;
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <returns></returns>
        private (BlobStore blobs, BackupStore backups) LoadIndex()
        {
            CloudBlobStoreDependencies blobStoreDependencies = new CloudBlobStoreDependencies(CloudInterop);
            BlobStore blobs = BlobStore.deserialize(CloudInterop.DownloadFileAsync(BackupBlobIndexFile).Result, blobStoreDependencies);
            CloudBackupStoreDependencies backupStoreDependencies = new CloudBackupStoreDependencies(CloudInterop, blobs);
            BackupStore backups = new BackupStore(backupStoreDependencies);
            return (blobs, backups);
        }

        public async void SaveBlobStoreIndex()
        {
            await CloudInterop.UploadFileAsync(BackupBlobIndexFile, Blobs.serialize());
        }

        public string ReadSetting(BackupSetting key)
        {
            using (var fs = GetSettingsFileStream())
            {
                return SettingsFileTools.ReadSetting(fs, key);
            }
        }

        public Dictionary<BackupSetting, string> ReadSettings()
        {
            using (var fs = GetSettingsFileStream())
            {
                return SettingsFileTools.ReadSettings(fs);
            }
        }

        public void WriteSetting(BackupSetting key, string value)
        {
            using (var fs = GetSettingsFileStream())
            {
                WriteSettingsFileStreamAsync(SettingsFileTools.WriteSetting(fs, key, value));
            }
        }

        public void ClearSetting(BackupSetting key)
        {
            using (var fs = GetSettingsFileStream())
            {
                SettingsFileTools.ClearSetting(fs, key);
            }
        }

        private Stream GetSettingsFileStream() => new MemoryStream(CloudInterop.DownloadFileAsync(SettingsFilename).Result);

        private async void WriteSettingsFileStreamAsync(byte[] data) => await CloudInterop.UploadFileAsync(SettingsFilename, data);
    }
}
