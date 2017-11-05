using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;

namespace BackupCore
{
    public class FSCoreDependencies : ICoreDependencies
    {
        /// <summary>
        /// The directory who's contents will be backed up.
        /// </summary>
        private string BackupPathSrc { get; set; }

        /// <summary>
        /// The directory in which to save the backup.
        /// </summary>
        private string BackupDstPath { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where blobs are stored. 
        /// </summary>
        private string BackupBlobDataDir { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where the lagern index is stored.
        /// </summary>
        private string BackupIndexDir { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where BackupStores are saved.
        /// </summary>
        private string BackupStoreDir { get; set; }

        /// <summary>
        /// The file containing the mapping of hashes to blob data.
        /// </summary>
        private string BackupBlobIndexFile { get; set; }

        /// <summary>
        /// The directory in which to save the cache.
        /// </summary>
        private string CachePath { get; set; }

        /// <summary>
        /// The directory in CachePath where blobs are stored.
        /// </summary>
        private string CacheBlobDataDir { get; set; }

        /// <summary>
        /// The directory in CachePath where the lagern cache index is stored.
        /// </summary>
        private string CacheIndexDir { get; set; }

        /// <summary>
        /// The directory in CacheIndexDir where BackupStores are saved.
        /// </summary>
        private string CacheBackupStoresDir { get; set; }

        /// <summary>
        /// The file in CacheIndexDir containing the mapping of hashes to blob data.
        /// </summary>
        private string CacheBlobIndexFile { get; set; }

        /// <summary>
        /// The name of the index directory for all lagern backups.
        /// </summary>
        public static readonly string IndexDirName = "index";
        /// <summary>
        /// The name of the backupstore directory for all lagern backups.
        /// </summary>
        public static readonly string BackupStoreDirName = "backupstores";
        /// <summary>
        /// The name of the blob directory for all lagern backups
        /// </summary>
        public static readonly string BlobDirName = "blobdata";
        
        /// <summary>
        /// The BlobStore in which data will be stored for this instance of Core.
        /// If available, represents BlobStore in the regular destination. Otherwise it is the cache BlobStore.
        /// </summary>
        public BlobStore DefaultBlobs { get; set; }
        /// <summary>
        /// The BackupStore in which backups will be recorded for this instance of Core.
        /// If available, represents BackupStore in the regular destination. Otherwise it is the cache BackupStore.
        /// </summary>
        public BackupStore DefaultBackups { get; set; }

        /// <summary>
        /// The BlobStore in the cache.
        /// </summary>
        public BlobStore CacheBlobs { get; set; }
        /// <summary>
        /// The BackupStore in the cache.
        /// </summary>
        public BackupStore CacheBackups { get; set; }

        /// <summary>
        /// True if the regular backup destination is available.
        /// If the destination is not available we attempt to use the cache.
        /// </summary>
        public bool DestinationAvailable { get; set; }

        public FSCoreDependencies(string src, string dst, string cache = null)
        {
            BackupPathSrc = src;
            BackupDstPath = dst;
            CachePath = cache;
        }

        public void InitializeNewDstAndCache(string bsname)
        {
            // Create lagern directory structure at destination if it doesn't already exist
            (string backupindexdir, string backupblobdatadir, string backupstoredir, string backupblobindexfile) = GetDestinationPaths(BackupDstPath);
            PrepBackupDstPath(BackupDstPath);

            if (File.Exists(Path.Combine(backupstoredir, bsname)))
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!File.Exists(backupblobindexfile))
            {
                FSBlobStoreDependencies blobStoreDependencies = new FSBlobStoreDependencies(backupblobdatadir);
                DefaultBlobs = new BlobStore(blobStoreDependencies);
                SaveDefaultBlobStoreIndex();
            }
            FSBackupStoreDependencies backupStoreDependencies = new FSBackupStoreDependencies(DefaultBlobs, backupstoredir);
            DefaultBackups = new BackupStore(backupStoreDependencies);
            DefaultBackups.SaveBackupSet(new BackupSet(), bsname);

            // Recurse once to initialize cache
            if (CachePath != null)
            {
                InitializeNewDstAndCache(bsname + Core.CacheSuffix);
            }
        }

        /// <summary>
        /// Creates needed directory structure at backup destination.
        /// </summary>
        /// <param name="dstpath"></param>
        /// <returns></returns>
        private static void PrepBackupDstPath(string dstpath)
        {
            (string id, string bsd, string bdd, _) = GetDestinationPaths(dstpath);
            // Make sure we have an index folder to write to later
            if (!Directory.Exists(id))
            {
                Directory.CreateDirectory(id);
            }
            // Make sure we have a backup list folder to write to later
            if (!Directory.Exists(bsd))
            {
                Directory.CreateDirectory(bsd);
            }
            if (!Directory.Exists(bdd))
            {
                Directory.CreateDirectory(bdd);
            }
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <param name="blobdatadir"></param>
        /// <param name="blobindexfile"></param>
        /// <param name="backupstoresdir"></param>
        /// <param name="iscahce"></param>
        /// <param name="continueorkill"></param>
        /// <returns></returns>
        private static (BlobStore blobs, BackupStore backups) LoadIndex(string blobdatadir, string blobindexfile,
            string backupstoresdir)
        {
            FSBlobStoreDependencies blobStoreDependencies = new FSBlobStoreDependencies(blobdatadir);
            BlobStore blobs = BlobStore.deserialize(File.ReadAllBytes(blobindexfile), blobStoreDependencies);
            FSBackupStoreDependencies backupStoreDependencies = new FSBackupStoreDependencies(blobs, backupstoresdir);
            BackupStore backups = new BackupStore(backupStoreDependencies);
            return (blobs, backups);
        }

        public void LoadDstAndCache()
        {
            // Attempt to initialize a Core instance to backup to dst
            try
            {
                (BackupIndexDir, BackupBlobDataDir, BackupStoreDir, BackupBlobIndexFile) = GetDestinationPaths(BackupDstPath);
                (DefaultBlobs, DefaultBackups) = LoadIndex(BackupBlobDataDir, BackupBlobIndexFile, BackupStoreDir);
                DestinationAvailable = true;
            }
            catch
            {
                // dst not available
                // error if no cache specified
                DestinationAvailable = false;
            }
            // No try/catch because chache should always be available if one is specified
            if (CachePath != null)
            {
                // Initialize cache
                (CacheIndexDir, CacheBlobDataDir, CacheBackupStoresDir, CacheBlobIndexFile) = GetDestinationPaths(CachePath);
                (CacheBlobs, CacheBackups) = LoadIndex(CacheBlobDataDir, CacheBlobIndexFile, CacheBackupStoresDir);
            }
            // If no destination, use cache as default
            if (!DestinationAvailable)
            {
                if (CachePath != null)
                {
                    DefaultBackups = CacheBackups;
                    DefaultBlobs = CacheBlobs;
                }
                else
                {
                    throw new Exception("Cannot initialize cache or dst");
                }
            }
        }

        private static (string indexdir, string blobdatadir, string backupstoresdir, string blobindexfile) GetDestinationPaths(string dstpath)
        {
            string id = Path.Combine(dstpath, IndexDirName);
            string bsd = Path.Combine(id, BackupStoreDirName);
            string bdd = Path.Combine(dstpath, BlobDirName);
            string bif = Path.Combine(id, "hashindex");
            return (id, bdd, bsd, bif);
        }

        public FileMetadata GetFileMetadata(string relpath)
        {
            return new FileMetadata(Path.Combine(BackupPathSrc, relpath.Substring(1)));
        }

        public IEnumerable<string> GetDirectoryFiles(string relpath)
        {
            return Directory.GetFiles(Path.Combine(BackupPathSrc, relpath.Substring(1))).Select(filepath => Path.GetFileName(filepath));
        }

        public FileStream GetFileData(string relpath)
        {
            return File.OpenRead(Path.Combine(BackupPathSrc, relpath));
        }

        public void SaveDefaultBlobStoreIndex()
        {
            if (DestinationAvailable)
            {
                using (FileStream fs = new FileStream(BackupBlobIndexFile, FileMode.Create, FileAccess.Write))
                {
                    using (BinaryWriter writer = new BinaryWriter(fs))
                    {
                        writer.Write(DefaultBlobs.serialize());
                    }
                }
            }
            else
            {
                SaveCacheBlobStoreIndex();
            }
        }

        public void SaveCacheBlobStoreIndex()
        {
            using (FileStream fs = new FileStream(CacheBlobIndexFile, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(CacheBlobs.serialize());
                }
            }
        }
    }
}
