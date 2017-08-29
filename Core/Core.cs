using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Collections.Concurrent;
using System.Threading;
using System.Security.Cryptography;

namespace BackupCore
{
    // Calling a public method that modifies blobs or backups automatically triggers an index save
    public class Core
    {
        /// <summary>
        /// The directory who's contents will be backed up.
        /// </summary>
        public string BackupPathSrc { get; set; }

        /// <summary>
        /// The directory in which to save the backup.
        /// </summary>
        public string BackupDstPath { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where blobs are stored. 
        /// </summary>
        public string BackupBlobDataDir { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where the lagern index is stored.
        /// </summary>
        public string BackupIndexDir { get; set; }

        /// <summary>
        /// The directory in BackupDstPath where BackupStores are saved.
        /// </summary>
        public string BackupStoresDir { get; set; }

        /// <summary>
        /// The file containing the mapping of hashes to blob data.
        /// </summary>
        public string BackupBlobIndexFile { get; set; }
        
        /// <summary>
        /// The directory in which to save the cache.
        /// </summary>
        public string CachePath { get; set; }

        /// <summary>
        /// The directory in CachePath where blobs are stored.
        /// </summary>
        public string CacheBlobDataDir { get; set; }

        /// <summary>
        /// The directory in CachePath where the lagern cache index is stored.
        /// </summary>
        public string CacheIndexDir { get; set; }

        /// <summary>
        /// The directory in CacheIndexDir where BackupStores are saved.
        /// </summary>
        public string CacheBackupStoresDir { get; set; }

        /// <summary>
        /// The file in CacheIndexDir containing the mapping of hashes to blob data.
        /// </summary>
        public string CacheBlobIndexFile { get; set; }

        /// <summary>
        /// The name of the index directory for all lagern backups.
        /// </summary>
        public static readonly string IndexDirName = "index";
        /// <summary>
        /// The name of the backupstore directory for all lagern backups.
        /// </summary>
        private static readonly string BackupStoreDirName = "backupstores";
        /// <summary>
        /// The name of the blob directory for all lagern backups
        /// </summary>
        private static readonly string BlobDirName = "blobdata";

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

        /// <summary>
        /// Initializes a new lagern Core. Core surfaces all public methods for use by programs implementing this library.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="src">Backup source directory</param>
        /// <param name="dst">Backup destination directory</param>
        /// <param name="continueorkill">
        /// Function that takes an error message as input.
        /// Meant to be used to optionally halt execution in case of errors.
        /// Is a safety mechanism so important data is not overwritten if the program
        /// decides to initialize a new index over an old one.
        /// TODO: Replace this with a flag like "allowoverwrite=[false]"
        ///     If false a special error class is thrown when a corrupted index is encountered.
        ///     The command line program would then handle the functionality of asking to overwrite.
        /// </param>
        public Core(string src, string dst, string cache=null, Action<string> continueorkill=null)
        {
            BackupPathSrc = src;
            BackupDstPath = dst;
            
            // Attempt to initialize a Core instance to backup to dst
            try
            {
                (BackupIndexDir, BackupBlobDataDir, BackupStoresDir, BackupBlobIndexFile) = PrepBackupDstPath(dst);
                (DefaultBlobs, DefaultBackups) = LoadIndex(BackupBlobDataDir, BackupBlobIndexFile, BackupStoresDir, false, continueorkill);
                DestinationAvailable = true;
            }
            catch (Exception e)
            {
                // dst not available
                // error if no cache specified
                DestinationAvailable = false;
                if (cache == null)
                {
                    throw e;
                }
                // Attempt to initialize a Core instance to backup in cache
                // If this fails we allow errors to bubble up
                CachePath = cache;
                (CacheIndexDir, CacheBlobDataDir, CacheBackupStoresDir, CacheBlobIndexFile) = PrepBackupDstPath(cache);
                (CacheBlobs, CacheBackups) = LoadIndex(CacheBlobDataDir, CacheBlobIndexFile, CacheBackupStoresDir, true, continueorkill);
                if (!DestinationAvailable)
                {
                    DefaultBackups = CacheBackups;
                    DefaultBlobs = CacheBlobs;
                }
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
            string backupstoresdir, bool iscahce=false, Action<string> continueorkill=null)
        {
            BlobStore blobs;
            // Create blob index and backup store
            if (File.Exists(blobindexfile))
            {
                try
                {
                    blobs = BlobStore.LoadFromFile(blobindexfile, blobdatadir);
                }
                catch (Exception)
                {
                    continueorkill?.Invoke("Failed to read blob index. Continuing may " +
                        "result in block index being overwritten and old blocks being lost.");
                    blobs = new BlobStore(blobindexfile, blobdatadir, iscahce);
                }
            }
            else // Safe to write a new file since one not already there
            {
                blobs = new BlobStore(blobindexfile, blobdatadir, iscahce);
            }

            BackupStore backups = new BackupStore(backupstoresdir, blobs);
            return (blobs, backups);
        }

        /// <summary>
        /// Creates needed directory structure at backup destination.
        /// </summary>
        /// <param name="dstpath"></param>
        /// <returns></returns>
        private static (string indexdir, string blobdatadir, string backupstoresdir, string blobindexfile) PrepBackupDstPath(string dstpath)
        {
            string id = Path.Combine(dstpath, IndexDirName);

            // Make sure we have an index folder to write to later
            if (!Directory.Exists(id))
            {
                Directory.CreateDirectory(id);
            }

            string bsd = Path.Combine(id, BackupStoreDirName);

            // Make sure we have a backup list folder to write to later
            if (!Directory.Exists(bsd))
            {
                Directory.CreateDirectory(bsd);
            }

            string bdd = Path.Combine(dstpath, BlobDirName);

            if (!Directory.Exists(bdd))
            {
                Directory.CreateDirectory(bdd);
            }

            string bif = Path.Combine(id, "hashindex");
            return (id, bdd, bsd, bif);
        }

        /// <summary>
        /// Performs a backup, parallelizing backup tasks and using all CPU cores as much as possible.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="message"></param>
        /// <param name="differentialbackup">True if we attempt to avoid scanning file data when the 
        /// data appears not to have been modified based on its metadata</param>
        /// <param name="trackpatterns">Rules determining which files</param>
        /// <param name="prev_backup_hash_prefix"></param>
        public void RunBackupAsync(string backupsetname, string message, bool differentialbackup=true, List<Tuple<int, string>> trackpatterns=null,
            string prev_backup_hash_prefix=null)
        {
            // The tree in which to store the new backup
            MetadataNode newmetatree = new MetadataNode(new FileMetadata(BackupPathSrc), null);
            
            // The queue of files who's contents must be examined
            BlockingCollection<string> scanfilequeue = new BlockingCollection<string>();
            // The queue of files who's contents will not be examined, instead a reference to a previous state of the file will be stored
            BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue = new BlockingCollection<Tuple<string, FileMetadata>>();
            // The queue of directories who's children will be examined
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();

            // Save cache to destination
            SyncCache(backupsetname, true);

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    previousbackup = DefaultBackups.GetBackupRecord(prev_backup_hash_prefix);
                }
                catch
                {
                    previousbackup = DefaultBackups.GetBackupRecord(backupsetname);
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(DefaultBlobs, previousbackup.MetadataTreeHash);
                    //Task getfilestask = Task.Run(() => GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, previousmtree, trackpaters));
                    GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, previousmtree, trackpatterns);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                //Task getfilestask = Task.Run(() => GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, null, trackpaters));
                GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, null, trackpatterns);
            }

            List<Task> backupops = new List<Task>();
            while (!directoryqueue.IsCompleted)
            {
                if (directoryqueue.TryTake(out string directory))
                {
                    // We do not backup diretories asychronously
                    // becuase a. they should not take long anyway
                    // and b. the metadatastore needs to have stored directories
                    // before it stores their children.
                    BackupDirectory(directory, newmetatree);
                }
            }
            while (!scanfilequeue.IsCompleted)
            {
                if (scanfilequeue.TryTake(out string file))
                {
                    //backupops.Add(Task.Run(() => BackupFileAsync(file, newmetatree)));
                    backupops.Add(Task.Run(() => BackupFileSync(file, newmetatree)));
                }
            }
            while (!noscanfilequeue.IsCompleted)
            {
                if (noscanfilequeue.TryTake(out Tuple<string, FileMetadata> dir_fmeta))
                {
                    newmetatree.AddFile(dir_fmeta.Item1, dir_fmeta.Item2);
                    if (DestinationAvailable)
                    {
                        DefaultBlobs.IncrementReferenceCount(dir_fmeta.Item2.FileHash, 1, true); // no files in cache store
                    }
                }
            }
            Task.WaitAll(backupops.ToArray());

            /*
            // Add new metadatatree to metastore
            byte[] newmtreebytes = newmetatree.serialize();
            //byte[] newmtreehash = Blobs.StoreDataAsync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            byte[] newmtreehash = Blobs.StoreDataSync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            */

            byte[] newmtreehash = newmetatree.Store(DefaultBlobs);

            DefaultBackups.AddBackup(backupsetname, message, newmtreehash, false);

            SyncCache(backupsetname, false);
            // Index save occurred during synccache
        }

        /// <summary>
        /// Saves both destination and cache blob indices (as available).
        /// </summary>
        public void SaveBlobIndices()
        {
            // Save "index"
            // Writeout all "dirty" cached index nodes
            try
            {
                DefaultBlobs.SaveToDisk();
                if (CacheBlobs != null && DestinationAvailable)
                {
                    CacheBlobs.SaveToDisk();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Performs a backup synchronously.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="message"></param>
        /// <param name="differentialbackup">True if we attempt to avoid scanning file data when the 
        /// data appears not to have been modified based on its metadata</param>
        /// <param name="trackpatterns">Rules determining which files</param>
        /// <param name="prev_backup_hash_prefix"></param>
        public void RunBackupSync(string backupsetname, string message, bool differentialbackup = true, List<Tuple<int, string>> trackpatterns = null,
            string prev_backup_hash_prefix = null)
        {
            // The tree in which to store the new backup
            MetadataNode newmetatree = new MetadataNode(new FileMetadata(BackupPathSrc), null);

            // The queue of files who's contents must be examined
            BlockingCollection<string> scanfilequeue = new BlockingCollection<string>();
            // The queue of files who's contents will not be examined, instead a reference to a previous state of the file will be stored
            BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue = new BlockingCollection<Tuple<string, FileMetadata>>();
            // The queue of directories who's children will be examined
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();

            // Save cache to destination
            SyncCache(backupsetname, true);

            if (differentialbackup)
            {
                BackupRecord previousbackup = DefaultBackups.GetBackupRecord(backupsetname);
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(DefaultBlobs, previousbackup.MetadataTreeHash);
                    GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, previousmtree, trackpatterns);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, null, trackpatterns);
            }

            while (!directoryqueue.IsCompleted)
            {
                if (directoryqueue.TryTake(out string directory))
                {
                    // We backup directories first because
                    // the metadatastore needs to have stored directories
                    // before it stores their children.
                    BackupDirectory(directory, newmetatree);
                }
            }
            while (!scanfilequeue.IsCompleted)
            {
                if (scanfilequeue.TryTake(out string file))
                {
                    BackupFileSync(file, newmetatree);
                }
            }
            while (!noscanfilequeue.IsCompleted)
            {
                if (noscanfilequeue.TryTake(out Tuple<string, FileMetadata> dir_fmeta))
                {
                    newmetatree.AddFile(dir_fmeta.Item1, dir_fmeta.Item2);
                    if (DestinationAvailable)
                    {
                        DefaultBlobs.IncrementReferenceCount(dir_fmeta.Item2.FileHash, 1, true);
                    }
                }
            }
            
            byte[] newmtreehash = newmetatree.Store(DefaultBlobs);

            DefaultBackups.AddBackup(backupsetname, message, newmtreehash, false);

            // Save just backups and metadata, no actual data to cache
            SyncCache(backupsetname, false);
            // Index save occurred during synccache
        }

        public void SyncCache(string backupsetname, bool cleardata)
        {
            if (CacheBackups != null)
            {
                if (DestinationAvailable)
                {
                    DefaultBackups.SyncCache(CacheBackups, backupsetname);
                    if (cleardata)
                    {
                        CacheBlobs.ClearData(new HashSet<string>(CacheBackups.GetBackupsAndMetadataReferencesAsStrings(backupsetname)));
                    }
                }
            }
            SaveBlobIndices();
        }

        // TODO: Alternate data streams associated with file -> save as ordinary data (will need changes to FileIndex)
        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public void RestoreFileOrDirectory(string backupsetname, string relfilepath, string restorepath, string backuphashprefix = null)
        {
            try
            {
                MetadataNode mtree = MetadataNode.Load(DefaultBlobs, DefaultBackups.GetBackupRecord(backupsetname, backuphashprefix).MetadataTreeHash);
                FileMetadata filemeta = mtree.GetFile(relfilepath);
                if (filemeta != null)
                {
                    byte[] filedata = DefaultBlobs.RetrieveData(filemeta.FileHash);
                    // The more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
                    using (FileStream writer = new FileStream(restorepath, FileMode.OpenOrCreate))
                    {
                        writer.Write(filedata, 0, filedata.Length);
                        // Flush the writer in order to get a correct stream position for truncating
                        writer.Flush();
                        // Set the stream length to the current position in order to truncate leftover data in original file
                        writer.SetLength(writer.Position);
                    }
                    filemeta.WriteOutMetadata(restorepath);
                }
                else
                {
                    MetadataNode dir = mtree.GetDirectory(relfilepath);
                    if (dir != null)
                    {
                        Directory.CreateDirectory(restorepath);
                        foreach (var childfile in dir.Files.Values)
                        {
                            RestoreFileOrDirectory(Path.Combine(relfilepath, childfile.FileName), Path.Combine(restorepath, childfile.FileName), backuphashprefix);
                        }
                        foreach (var childdir in dir.Directories.Keys)
                        {
                            RestoreFileOrDirectory(Path.Combine(relfilepath, childdir), Path.Combine(restorepath, childdir), backuphashprefix);
                        }
                        dir.DirMetadata.WriteOutMetadata(restorepath); // Set metadata after finished changing contents (postorder)
                    }
                }
            }
            catch (Exception)
            {
                if (!DestinationAvailable)
                {
                    Console.WriteLine("Error restoring file or directory." +
                        "Try restoring again with backup drive present");
                }
                else
                {
                    Console.WriteLine("Error restoring file or directory.");
                }
                throw;
            }
        }

        /// <summary>
        /// Returns the paths of files and directories in path to be backed up.
        /// </summary>
        /// <param name="scanfilequeue"></param>
        /// <param name="noscanfilequeue"></param>
        /// <param name="directoryqueue"></param>
        /// <param name="path"></param>
        /// <param name="previousmtree"></param>
        /// <param name="trackpaterns"></param>
        protected void GetFilesAndDirectories(BlockingCollection<string> scanfilequeue, BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue, 
            BlockingCollection<string> directoryqueue, string path=null, MetadataNode previousmtree=null, List<Tuple<int, string>> trackpaterns=null)
        {
            if (path == null)
            {
                path = BackupPathSrc;
            }

            // TODO: Bigger inital stack size?
            Stack<string> dirs = new Stack<string>(20);

            dirs.Push(path);

            while (dirs.Count > 0)
            {
                string currentDir = dirs.Pop();
                string[] subDirs;
                try
                {
                    subDirs = Directory.GetDirectories(currentDir);
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }
                catch (DirectoryNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                foreach (var sd in subDirs)
                {
                    bool trackdir = true;
                    if (trackpaterns != null)
                    {
                        trackdir = CheckTrackAnyDirectoryChild(sd.Substring(BackupPathSrc.Length + 1), trackpaterns);
                    }
                    if (trackdir)
                    {
                        dirs.Push(sd);
                        string relpath = sd.Substring(BackupPathSrc.Length + 1);
                        directoryqueue.Add(relpath);
                    }
                }

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(currentDir);
                }
                catch (UnauthorizedAccessException e)
                {
                    Console.WriteLine(e.Message);
                }
                catch (DirectoryNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }

                foreach (var file in files)
                {
                    int trackclass = 2;
                    if (trackpaterns != null)
                    {
                        trackclass = FileTrackClass(file.Substring(BackupPathSrc.Length + 1), trackpaterns);
                    }
                    // Convert file path to a relative path
                    string relpath = file.Substring(BackupPathSrc.Length + 1);
                    try // We (may) read the file's metadata here so wrap errors
                    {
                        switch (trackclass)
                        {
                            case 0: // ignore file completely
                                break;
                            case 1: // Dont scan if we have a previous version
                                bool dontscan = false;
                                if (previousmtree != null)
                                {
                                    FileMetadata previousfm = previousmtree.GetFile(relpath);
                                    FileMetadata curfm = new FileMetadata(Path.Combine(BackupPathSrc, relpath));
                                    if (previousfm != null)
                                    {
                                        noscanfilequeue.Add(new Tuple<string, FileMetadata>(Path.GetDirectoryName(relpath), previousfm));
                                        dontscan = true;
                                    }
                                }
                                if (!dontscan)
                                {
                                    scanfilequeue.Add(relpath);
                                }
                                break;
                            case 2: // Dont scan if we have a previous version and its metadata indicates no change
                                dontscan = false;
                                if (previousmtree != null)
                                {
                                    FileMetadata previousfm = previousmtree.GetFile(relpath);
                                    FileMetadata curfm = new FileMetadata(Path.Combine(BackupPathSrc, relpath));
                                    if (previousfm != null && previousfm.FileSize == curfm.FileSize
                                        && previousfm.DateModifiedUTC == curfm.DateModifiedUTC)
                                    {
                                        noscanfilequeue.Add(new Tuple<string, FileMetadata>(Path.GetDirectoryName(relpath), previousfm));
                                        dontscan = true;
                                    }
                                }
                                if (!dontscan)
                                {
                                    scanfilequeue.Add(relpath);
                                }
                                break;
                            case 3: // Scan file
                                scanfilequeue.Add(relpath);
                                break;
                            default:
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
            directoryqueue.CompleteAdding();
            scanfilequeue.CompleteAdding();
            noscanfilequeue.CompleteAdding();
        }

        /// <summary>
        /// Matches a wildcard pattern to path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="pattern"></param>
        /// <returns></returns>
        public static bool PatternMatchesPath(string path, string pattern)
        {
            if (pattern == "*")
            {
                return true;
            }
            int wildpos = pattern.IndexOf('*');
            if (wildpos >= 0)
            {
                string prefix = pattern.Substring(0, wildpos);
                if (prefix.Length > 0)
                {
                    if (path.Length >= prefix.Length && prefix == path.Substring(0, prefix.Length))
                    {
                        string wsuffix = pattern.Substring(wildpos, pattern.Length - wildpos);
                        return PatternMatchesPath(path.Substring(prefix.Length), wsuffix);
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    // Strip wildcard
                    pattern = pattern.Substring(1);
                    while (path.Length > 0)
                    {
                        if (PatternMatchesPath(path, pattern))
                        {
                            return true;
                        }
                        path = path.Substring(1);
                    }
                    return false;
                }
            }
            else
            {
                return path == pattern;
            }
        }

        /// <summary>
        /// Checks whether this file is tracked based on trackpattern
        /// </summary>
        /// <param name="file"></param>
        /// <param name="trackpatterns"></param>
        /// <returns></returns>
        public static int FileTrackClass(string file, List<Tuple<int, string>> trackpatterns)
        {
            int trackclass = 2;
            foreach (var pattern in trackpatterns)
            {
                if (PatternMatchesPath(file, pattern.Item2))
                {
                    trackclass = pattern.Item1;
                }
            }
            return trackclass;
        }

        /// <summary>
        /// Checks whether any directory child (files, sub directories, files in sub directories...) might possibly be tracked.
        /// </summary>
        /// <param name="folder"></param>
        /// <param name="trackpatterns"></param>
        /// <returns></returns>
        public static bool CheckTrackAnyDirectoryChild(string directory, List<Tuple<int, string>> trackpatterns)
        {
            bool track = true;
            foreach (var pattern in trackpatterns)
            {
                if (track)
                {
                    if (pattern.Item1 == 0)
                    {
                        // Can only exclude if there is a trailing wildcard after 
                        // the rest of the pattern matches to this directory
                        if (pattern.Item2[pattern.Item2.Length - 1] == '*')
                        {
                            if (pattern.Item2.Length == 1) // just "*"
                            {
                                track = false;
                            }
                            else if (pattern.Item2.Substring(pattern.Item2.Length - 2) == "/*") // trailing wildcard must come immediately after a slash /*
                            {
                                if (PatternMatchesPath(directory, pattern.Item2.Substring(0, pattern.Item2.Length - 2))) 
                                {
                                    track = false;
                                }
                            }
                        }
                    }
                }
                else
                {
                    if (pattern.Item1 != 0)
                    {
                        int wildpos = pattern.Item2.IndexOf('*');
                        if (wildpos == 0)
                        {
                            track = true;
                        }
                        else if (wildpos > 0)
                        {
                            string prefix = pattern.Item2.Substring(0, wildpos);
                            if (prefix.Length >= directory.Length)
                            {
                                if (prefix.StartsWith(directory))
                                {
                                    track = true;
                                }
                            }
                            else
                            {
                                if (directory.StartsWith(prefix))
                                {
                                    track = true;
                                }
                            }
                        }

                    }
                }
            }
            return track;
        }

        /// <summary>
        /// Calculates the size of the blobs and child blobs referenced by the given hash.
        /// </summary>
        /// <param name="backuphashstring"></param>
        /// <returns>(Size of all referenced blobs, size of blobs referenced only by the given hash and its children)</returns>
        public (int allreferencesizes, int uniquereferencesizes) GetBackupSizes(string backuphashstring)
        {
            return DefaultBlobs.GetSizes(HashTools.HexStringToByteArray(backuphashstring));
        }

        /// <summary>
        /// Backup a directory to the given metadatanode
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        private void BackupDirectory(string relpath, MetadataNode mtree)
        {
            mtree.AddDirectory(Path.GetDirectoryName(relpath), new FileMetadata(Path.Combine(BackupPathSrc, relpath)));
        }

        // TODO: This has problems (see todo on BlobStore.StoreDataAsync()
        protected void BackupFileAsync(string relpath, MetadataNode mtree)
        {
            throw new NotImplementedException(); // Dont use until BlobStore.StoreDataAsync() fixed
            // NOTE: If more detailed error handling is added, replace this try/catch and the 
            // equivelent one in BackupFileSync with a single method for getting a stream
            try
            {
                FileStream readerbuffer = File.OpenRead(Path.Combine(BackupPathSrc, relpath));
                byte[] filehash = DefaultBlobs.StoreDataAsync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
                BackupFileMetadata(relpath, filehash, mtree);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Backup a file into the given metadatanode
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        protected void BackupFileSync(string relpath, MetadataNode mtree)
        {
            try
            {
                FileStream readerbuffer = File.OpenRead(Path.Combine(BackupPathSrc, relpath));
                byte[] filehash = DefaultBlobs.StoreDataSync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
                BackupFileMetadata(relpath, filehash, mtree);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        /// <summary>
        /// Loads metadata from a file and adds it to the metdata tree.
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="filehash"></param>
        /// <param name="mtree"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="System.Security.SecurityException"/>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="UnauthorizedAccessException"/>
        /// <exception cref="PathTooLongException"/>
        /// <exception cref="NotSupportedException"/>
        protected void BackupFileMetadata(string relpath, byte[] filehash, MetadataNode mtree)
        {
            FileMetadata fm = new FileMetadata(Path.Combine(BackupPathSrc, relpath))
            {
                FileHash = filehash
            };
            lock (DefaultBackups)
            {
                mtree.AddFile(Path.GetDirectoryName(relpath), fm);
            }
        }
        
        /// <summary>
        /// Retrieves list of backups from a backupset.
        /// </summary>
        /// <returns>A list of tuples representing the backup times and their associated messages.</returns>
        public IEnumerable<Tuple<string, DateTime, string>> GetBackups(string backupsetname)
        {// TODO: does this need to exist here
            List<Tuple<string, DateTime, string>> backups = new List<Tuple<string, DateTime, string>>();
            foreach (var backup in DefaultBackups.LoadBackupSet(backupsetname).Backups)
            {
                var br = DefaultBackups.GetBackupRecord(backupsetname, backup.Item1);
                backups.Add(new Tuple<string, DateTime, string>(HashTools.ByteArrayToHexViaLookup32(backup.Item1).ToLower(),
                    br.BackupTime, br.BackupMessage));
            }
            return backups;
        }

        /// <summary>
        /// Remove a backup from the BackupStore and its data from the BlobStore.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="backuphashprefix"></param>
        public void RemoveBackup(string backupsetname, string backuphashprefix)
        {
            DefaultBackups.RemoveBackup(backupsetname, backuphashprefix);
            // TODO: remove from cache or require sync?
            SaveBlobIndices();
        }

        /// <summary>
        /// Transfer a backupset and its data to a new location.
        /// </summary>
        /// <param name="src">The Core containing the backup store (and backing blobstore) to be transferred.</param>
        /// <param name="dst">The lagern directory you wish to transfer to.</param>
        public void TransferBackupSet(string backupsetname, string dst, bool includefiles, BlobStore dstblobs=null)
        {
            (string dstbackupindexdir, string dstblobdatadir, string dstbackupstoresdir, _) = PrepBackupDstPath(dst);
            File.Copy(Path.Combine(BackupStoresDir, backupsetname), Path.Combine(dstbackupstoresdir, backupsetname));

            if (dstblobs == null)
            {
                // Now actually transfer backups
                dstblobs = new Core(backupsetname, null, dst).DefaultBlobs;
            }
            foreach (var backup in DefaultBackups.LoadBackupSet(backupsetname).Backups)
            {
                DefaultBlobs.TransferBackup(dstblobs, backup.Item1, includefiles & !backup.Item2);
            }
            dstblobs.SaveToDisk();
        }
    }
}
