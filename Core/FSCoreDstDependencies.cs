using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BackupCore
{
    public class FSCoreDstDependencies : ICoreDstDependencies
    {
        public BlobStore Blobs { get; set; }

        public BackupStore Backups { get; set; }

        private IFSInterop FSInterop { get; set; }

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
        
        private string DstSettingsFile { get; set; }

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
        /// The name of the file holding the blobstore index
        /// </summary>
        public static readonly string BlobStoreIndexFilename = "hashindex";

        public static readonly string SettingsDirectoryName = ".lagern";

        public static readonly string SettingsFilename = ".settings";

        private FSCoreDstDependencies(string dst, IFSInterop fsinterop)
        {
            BackupDstPath = dst;
            FSInterop = fsinterop;
        }

        public static FSCoreDstDependencies InitializeNew(string bsname, string dst, IFSInterop fsinterop, bool cacheused)
        {
            FSCoreDstDependencies ddeps = new FSCoreDstDependencies(dst, fsinterop);
            // Create lagern directory structure at destination if it doesn't already exist
            (ddeps.BackupIndexDir, ddeps.BackupBlobDataDir, ddeps.BackupStoreDir, ddeps.BackupBlobIndexFile) = GetDestinationPaths(ddeps.BackupDstPath);
            ddeps.PrepBackupDstPath(ddeps.BackupDstPath);

            if (ddeps.FSInterop.FileExists(Path.Combine(ddeps.BackupStoreDir, bsname)))
            {
                throw new Exception("A backup set of the given name already exists at the destination");
            }
            if (!ddeps.FSInterop.FileExists(ddeps.BackupBlobIndexFile))
            {
                FSBlobStoreDependencies blobStoreDependencies = new FSBlobStoreDependencies(ddeps.FSInterop, ddeps.BackupBlobDataDir);
                ddeps.Blobs = new BlobStore(blobStoreDependencies);
                ddeps.SaveBlobStoreIndex();
            }
            FSBackupStoreDependencies backupStoreDependencies = new FSBackupStoreDependencies(ddeps.FSInterop, ddeps.Blobs, ddeps.BackupStoreDir);
            ddeps.Backups = new BackupStore(backupStoreDependencies);
            ddeps.Backups.SaveBackupSet(new BackupSet(cacheused), bsname);
            return ddeps;
        }

        public static FSCoreDstDependencies Load(string dst, IFSInterop fsinterop)
        {
            FSCoreDstDependencies ddeps = new FSCoreDstDependencies(dst, fsinterop);
            (ddeps.BackupIndexDir, ddeps.BackupBlobDataDir, ddeps.BackupStoreDir, ddeps.BackupBlobIndexFile) = GetDestinationPaths(ddeps.BackupDstPath);
            (ddeps.Blobs, ddeps.Backups) = ddeps.LoadIndex();
            return ddeps;
        }

        /// <summary>
        /// Creates needed directory structure at backup destination.
        /// </summary>
        /// <param name="dstpath"></param>
        /// <returns></returns>
        protected void PrepBackupDstPath(string dstpath)
        {
            (string id, string bsd, string bdd, _) = GetDestinationPaths(dstpath);
            // Make sure we have an index folder to write to later
            if (!FSInterop.DirectoryExists(id))
            {
                FSInterop.CreateDirectoryIfNotExists(id);
            }
            // Make sure we have a backup list folder to write to later
            if (!FSInterop.DirectoryExists(bsd))
            {
                FSInterop.CreateDirectoryIfNotExists(bsd);
            }
            if (!FSInterop.DirectoryExists(bdd))
            {
                FSInterop.CreateDirectoryIfNotExists(bdd);
            }
        }

        /// <summary>
        /// Loads a lagern index.
        /// </summary>
        /// <returns></returns>
        private (BlobStore blobs, BackupStore backups) LoadIndex()
        {
            FSBlobStoreDependencies blobStoreDependencies = new FSBlobStoreDependencies(FSInterop, BackupBlobDataDir);
            BlobStore blobs = BlobStore.deserialize(FSInterop.ReadAllFileBytes(BackupBlobIndexFile), blobStoreDependencies);
            FSBackupStoreDependencies backupStoreDependencies = new FSBackupStoreDependencies(FSInterop, blobs, BackupStoreDir);
            BackupStore backups = new BackupStore(backupStoreDependencies);
            return (blobs, backups);
        }

        protected static (string indexdir, string blobdatadir, string backupstoresdir, string blobindexfile) GetDestinationPaths(string dstpath)
        {
            string id = Path.Combine(dstpath, IndexDirName);
            string bsd = Path.Combine(id, BackupStoreDirName);
            string bdd = Path.Combine(dstpath, BlobDirName);
            string bif = Path.Combine(id, BlobStoreIndexFilename);
            return (id, bdd, bsd, bif);
        }

        public void SaveBlobStoreIndex()
        {
            FSInterop.OverwriteOrCreateFile(BackupBlobIndexFile, Blobs.serialize());
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
                 WriteSettingsFileStream(SettingsFileTools.WriteSetting(fs, key, value));
            }
        }

        public void ClearSetting(BackupSetting key)
        {
            using (var fs = GetSettingsFileStream())
            {
                SettingsFileTools.ClearSetting(fs, key);
            }
        }

        private Stream GetSettingsFileStream() => new FileStream(DstSettingsFile, FileMode.Open);

        private void WriteSettingsFileStream(byte[] data) => FSInterop.OverwriteOrCreateFile(DstSettingsFile, data);
    }
}
