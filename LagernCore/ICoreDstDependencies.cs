using System.Collections.Generic;

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

        void SaveBlobStoreIndex();
        
        string ReadSetting(BackupSetting key);

        Dictionary<BackupSetting, string> ReadSettings();

        void WriteSetting(BackupSetting key, string value);

        void ClearSetting(BackupSetting key);
    }
}
