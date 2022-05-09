using LagernCore.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    public class CoreDstDependencies : ICoreDstDependencies
    {
        public BlobStore Blobs { get; set; }

        public BackupStore Backups { get; set; }

        public IDstFSInterop DstFSInterop { get; private set; }

        // Supressing this check because Blobs and Backups must be set to non-null before use publicly.
        #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        private CoreDstDependencies(IDstFSInterop dstinterop)
        #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            DstFSInterop = dstinterop;
        }

        public static async Task<CoreDstDependencies> InitializeNew(string bsname, bool cache, IDstFSInterop dstinterop, bool cacheused=false)
        {
            CoreDstDependencies destdeps = new(dstinterop);

            if (await destdeps.DstFSInterop.IndexFileExistsAsync(bsname, IndexFileType.BackupSet))
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!await destdeps.DstFSInterop.IndexFileExistsAsync(null, IndexFileType.BlobIndex))
            {
                BlobStoreDependencies blobStoreDependencies = new(destdeps.DstFSInterop);
                destdeps.Blobs = new BlobStore(blobStoreDependencies);
                await destdeps.SaveBlobStoreIndex();
            }
            else
            {
                throw new Exception(); // TODO: Exception message
            }
            BackupStoreDependencies backupStoreDependencies = new(destdeps.DstFSInterop, destdeps.Blobs);
            destdeps.Backups = new BackupStore(backupStoreDependencies);
            await destdeps.Backups.SaveBackupSet(new BackupSet(cacheused), new BackupSetReference(bsname, false, false, false));
            return destdeps;
        }

        public static async Task<CoreDstDependencies> Load(IDstFSInterop dstinterop, bool cacheused = false)
        {
            CoreDstDependencies destdeps = new(dstinterop);
            // Would possibly load a cached blobindex file here
            (destdeps.Blobs, destdeps.Backups) = await destdeps.LoadIndex();
            return destdeps;
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <returns></returns>
        private async Task<(BlobStore blobs, BackupStore backups)> LoadIndex()
        {
            BlobStoreDependencies blobStoreDependencies = new(DstFSInterop);
            BlobStore blobs = BlobStore.Deserialize(await DstFSInterop.LoadIndexFileAsync(null, IndexFileType.BlobIndex), blobStoreDependencies);
            BackupStoreDependencies backupStoreDependencies = new(DstFSInterop, blobs);
            BackupStore backups = new(backupStoreDependencies);
            return (blobs, backups);
        }

        public async Task<string> ReadSetting(BackupSetting key)
        {
            using var fs = await GetSettingsFileStream();
            return await SettingsFileTools.ReadSetting(fs, key);
        }

        public async Task<Dictionary<BackupSetting, string>> ReadSettings()
        {
            using var fs = await GetSettingsFileStream();
            return await SettingsFileTools.ReadSettings(fs);
        }

        public async Task SaveBlobStoreIndex()
        {
            await DstFSInterop.StoreIndexFileAsync(null, IndexFileType.BlobIndex, Blobs.Serialize());
        }

        public async Task WriteSetting(BackupSetting key, string value)
        {
            using var fs = await GetSettingsFileStream();
            await WriteSettingsFileStreamAsync(await SettingsFileTools.WriteSetting(fs, key, value));
        }

        public async Task ClearSetting(BackupSetting key)
        {
            using var fs = await GetSettingsFileStream();
            await WriteSettingsFileStreamAsync(await SettingsFileTools.ClearSetting(fs, key));
        }

        private async Task<Stream> GetSettingsFileStream() => 
            new MemoryStream(await DstFSInterop.LoadIndexFileAsync(null, IndexFileType.SettingsFile));

        private async Task WriteSettingsFileStreamAsync(byte[] data) => 
            await DstFSInterop.StoreIndexFileAsync(null, IndexFileType.SettingsFile, data);
    }
}
