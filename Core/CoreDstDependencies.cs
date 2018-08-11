using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BackupCore
{
    public class CoreDstDependencies : ICoreDstDependencies
    {
        public BlobStore Blobs { get; set; }
        public BackupStore Backups { get; set; }

        private IDstFSInterop DstFSInterop { get; set; }


        private CoreDstDependencies(IDstFSInterop dstinterop)
        {
            DstFSInterop = dstinterop;
        }

        public static CoreDstDependencies InitializeNew(string bsname, IDstFSInterop dstinterop, bool cacheused=false)
        {
            CoreDstDependencies destdeps = new CoreDstDependencies(dstinterop);

            if (destdeps.DstFSInterop.IndexFileExistsAsync(bsname, IndexFileType.BackupSet).Result)
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!destdeps.DstFSInterop.IndexFileExistsAsync(null, IndexFileType.BlobIndex).Result)
            {
                BlobStoreDependencies blobStoreDependencies = new BlobStoreDependencies(destdeps.DstFSInterop);
                destdeps.Blobs = new BlobStore(blobStoreDependencies);
                destdeps.SaveBlobStoreIndex();
            }
            else
            {
                throw new Exception(); // TODO: Exception message
            }
            BackupStoreDependencies backupStoreDependencies = new BackupStoreDependencies(destdeps.DstFSInterop, destdeps.Blobs);
            destdeps.Backups = new BackupStore(backupStoreDependencies);
            destdeps.Backups.SaveBackupSet(new BackupSet(cacheused), bsname);
            return destdeps;
        }

        public static CoreDstDependencies Load(IDstFSInterop dstinterop, bool cacheused = false)
        {
            CoreDstDependencies destdeps = new CoreDstDependencies(dstinterop);
            // Would possibly load a cached blobindex file here
            (destdeps.Blobs, destdeps.Backups) = destdeps.LoadIndex();
            return destdeps;
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <returns></returns>
        private (BlobStore blobs, BackupStore backups) LoadIndex()
        {
            BlobStoreDependencies blobStoreDependencies = new BlobStoreDependencies(DstFSInterop);
            BlobStore blobs = BlobStore.deserialize(DstFSInterop.LoadIndexFileAsync(null, IndexFileType.BlobIndex).Result, blobStoreDependencies);
            BackupStoreDependencies backupStoreDependencies = new BackupStoreDependencies(DstFSInterop, blobs);
            BackupStore backups = new BackupStore(backupStoreDependencies);
            return (blobs, backups);
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

        public void SaveBlobStoreIndex()
        {
            DstFSInterop.StoreIndexFileAsync(null, IndexFileType.BlobIndex, Blobs.serialize());
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
                WriteSettingsFileStreamAsync(SettingsFileTools.ClearSetting(fs, key));
            }
        }

        private Stream GetSettingsFileStream() => new MemoryStream(DstFSInterop.LoadIndexFileAsync(null, IndexFileType.SettingsFile).Result);

        private void WriteSettingsFileStreamAsync(byte[] data) => DstFSInterop.StoreIndexFileAsync(null, IndexFileType.SettingsFile, data);
    }
}
