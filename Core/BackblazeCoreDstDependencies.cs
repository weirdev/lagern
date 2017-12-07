using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BackupCore
{
    public class BackblazeCoreDstDependencies : ICoreDstDependencies
    {
        public BlobStore Blobs { get; set; }
        public BackupStore Backups { get; set; }

        private BackblazeInterop BBInterop { get; set; }

        private static readonly string BackupBlobIndexFile = "hashindex";
        private static readonly string SettingsFilename = ".settings";

        private BackblazeCoreDstDependencies(BackblazeInterop bbinterop)
        {
            BBInterop = bbinterop;
        }

        public static BackblazeCoreDstDependencies InitializeNew(string bsname, BackblazeInterop bbinterop, bool cacheused)
        {
            BackblazeCoreDstDependencies bbdeps = new BackblazeCoreDstDependencies(bbinterop);

            if (bbdeps.BBInterop.FileExists(bsname).Result)
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!bbdeps.BBInterop.FileExists(BackupBlobIndexFile).Result)
            {
                BackblazeBlobStoreDependencies blobStoreDependencies = new BackblazeBlobStoreDependencies(bbdeps.BBInterop);
                bbdeps.Blobs = new BlobStore(blobStoreDependencies);
                bbdeps.SaveBlobStoreIndex();
            }
            else
            {
                throw new Exception();
            }
            BackblazeBackupStoreDependencies backupStoreDependencies = new BackblazeBackupStoreDependencies(bbdeps.BBInterop, bbdeps.Blobs);
            bbdeps.Backups = new BackupStore(backupStoreDependencies);
            bbdeps.Backups.SaveBackupSet(new BackupSet(cacheused), bsname);
            return bbdeps;
        }

        public static BackblazeCoreDstDependencies Load(BackblazeInterop bbinterop, bool cacheused = false)
        {
            BackblazeCoreDstDependencies bbdeps = new BackblazeCoreDstDependencies(bbinterop);
            // Would possibly load a cached blobindex file here
            (bbdeps.Blobs, bbdeps.Backups) = bbdeps.LoadIndex();
            return bbdeps;
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <returns></returns>
        private (BlobStore blobs, BackupStore backups) LoadIndex()
        {
            BackblazeBlobStoreDependencies blobStoreDependencies = new BackblazeBlobStoreDependencies(BBInterop);
            BlobStore blobs = BlobStore.deserialize(BBInterop.DownloadFile(BackupBlobIndexFile).Result, blobStoreDependencies);
            BackblazeBackupStoreDependencies backupStoreDependencies = new BackblazeBackupStoreDependencies(BBInterop, blobs);
            BackupStore backups = new BackupStore(backupStoreDependencies);
            return (blobs, backups);
        }

        public async void SaveBlobStoreIndex()
        {
            await BBInterop.UploadFileAsync(BackupBlobIndexFile, Blobs.serialize());
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

        private Stream GetSettingsFileStream() => new MemoryStream(BBInterop.DownloadFile(SettingsFilename).Result);

        private async void WriteSettingsFileStreamAsync(byte[] data) => await BBInterop.UploadFileAsync(SettingsFilename, data);
    }
}
