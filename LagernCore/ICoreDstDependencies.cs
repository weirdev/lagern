using System.Collections.Generic;
using System.Threading.Tasks;

namespace BackupCore
{
    public interface ICoreDstDependencies
    {
        /// <summary>
        /// The BlobStore in which data will be stored for this instance of Core.
        /// If available, represents BlobStore in the regular destination. Otherwise it is the cache BlobStore.
        /// </summary>
        BlobStore Blobs { get; set; }

        /// <summary>
        /// The BackupStore in which backups will be recorded for this instance of Core.
        /// If available, represents BackupStore in the regular destination. Otherwise it is the cache BackupStore.
        /// </summary>
        BackupStore Backups { get; set; }

        IDstFSInterop DstFSInterop { get; }

        Task SaveBlobStoreIndex();
        
        Task<string> ReadSetting(BackupSetting key);

        Task<Dictionary<BackupSetting, string>> ReadSettings();

        Task WriteSetting(BackupSetting key, string value);

        Task ClearSetting(BackupSetting key);
    }
}
