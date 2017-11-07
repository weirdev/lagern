using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    public interface ICoreDependencies
    {
        /// <summary>
        /// The BlobStore in which data will be stored for this instance of Core.
        /// If available, represents BlobStore in the regular destination. Otherwise it is the cache BlobStore.
        /// </summary>
        BlobStore DefaultBlobs { get; set; }
        /// <summary>
        /// The BackupStore in which backups will be recorded for this instance of Core.
        /// If available, represents BackupStore in the regular destination. Otherwise it is the cache BackupStore.
        /// </summary>
        BackupStore DefaultBackups { get; set; }

        /// <summary>
        /// The BlobStore in the cache.
        /// </summary>
        BlobStore CacheBlobs { get; set; }
        /// <summary>
        /// The BackupStore in the cache.
        /// </summary>
        BackupStore CacheBackups { get; set; }

        /// <summary>
        /// True if the regular backup destination is available.
        /// If the destination is not available we attempt to use the cache.
        /// </summary>
        bool DestinationAvailable { get; set; }

        void LoadDstAndCache();

        void InitializeNewDstAndCache(string bsname);

        void SaveDefaultBlobStoreIndex();

        void SaveCacheBlobStoreIndex();

        FileMetadata GetFileMetadata(string relpath);

        IEnumerable<string> GetDirectoryFiles(string relpath);

        IEnumerable<string> GetSubDirectories(string relpath);

        Stream GetFileData(string relpath);
    }
}
