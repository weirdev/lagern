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
                // Attempt to initialize a Core instance to backup in cache
                // If this fails we allow errors to bubble up
                CachePath = cache ?? throw e;
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

        public List<(string path, FileMetadata.FileStatus change)> GetWTStatus(string backupsetname, bool differentialbackup = true,
            bool usetrackclasses=true, List <Tuple<int, string>> trackpatterns = null, string prev_backup_hash_prefix = null)
        {
            MetadataNode deltatree = null;

            if (usetrackclasses)
            {
                try
                {
                    trackpatterns = GetTrackClasses();
                }
                catch (Exception)
                {
                    trackpatterns = null;
                }
            }

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    previousbackup = DefaultBackups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = DefaultBackups.GetBackupRecord(backupsetname);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(DefaultBlobs, previousbackup.MetadataTreeHash);
                    deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, previousmtree);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, null);
            }
            List<(string path, FileMetadata.FileStatus change)> changes = new List<(string path, FileMetadata.FileStatus change)>();
            GetDeltaNodeChanges(Path.DirectorySeparatorChar.ToString(), deltatree);

            return changes;

            // Recursive helper function to traverse delta tree
            void GetDeltaNodeChanges(string relpath, MetadataNode parent)
            {
                if (parent.DirMetadata.Changes == null)
                {
                    throw new Exception("Reached metadata without delta");
                }
                var status = parent.DirMetadata.Changes.Value.status;
                if (status == FileMetadata.FileStatus.Deleted)
                {
                    changes.Add((relpath, status));
                    // If deleted dont handle children
                }
                else
                {
                    // Not deleted so will handle children
                    if (status != FileMetadata.FileStatus.Unchanged)
                    {
                        changes.Add((relpath, status));
                    }
                    foreach (var file in parent.Files.Values)
                    {
                        if (file.Changes == null)
                        {
                            throw new Exception("Reached metadata without delta");
                        }
                        var fstatus = file.Changes.Value.status;
                        if (fstatus != FileMetadata.FileStatus.Unchanged)
                        {
                            changes.Add((Path.Combine(relpath, file.FileName), fstatus));
                        } 
                    }
                    foreach (var dir in parent.Directories.Values)
                    {
                        GetDeltaNodeChanges(Path.Combine(relpath, dir.DirMetadata.FileName) + Path.DirectorySeparatorChar, dir);
                    }
                }
            }
        }
        
        /// <summary>
        /// Gets the working tree status, returns a list of (paths, FileStatus)
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <returns>List of mod</returns>
        private MetadataNode GetDeltaMetadataTree(string backupsetname, List<Tuple<int, string>> trackpatterns = null,
            MetadataNode previousmtree=null) // TODO: Add detailed information about nature of differences with prev backup
        {
            // Save cache to destination
            SyncCache(backupsetname, true);

            MetadataNode deltamtree = previousmtree;
            Queue<(string path, MetadataNode node)> deltamnodequeue = new Queue<(string path, MetadataNode node)>();
            if (deltamtree != null)
            {
                // We always assume the matching of deltatree root to fs backup root is valid
                // So make the name equal, and set status to metadatachange if they were different
                FileMetadata dirmetadata = new FileMetadata(BackupPathSrc);
                if (deltamtree.DirMetadata.FileName != dirmetadata.FileName)
                {
                    deltamtree.DirMetadata.Changes = (FileMetadata.FileStatus.MetadataChange, dirmetadata);
                }
                deltamnodequeue.Enqueue((Path.DirectorySeparatorChar.ToString(), deltamtree));
            }
            else
            {
                deltamtree = new MetadataNode(new FileMetadata(BackupPathSrc), null);
                deltamnodequeue.Enqueue((Path.DirectorySeparatorChar.ToString(), deltamtree));
            }
            while (deltamnodequeue.Count > 0)
            {
                (string reldirpath, MetadataNode deltanode) = deltamnodequeue.Dequeue();
                string absolutedirpath = Path.Combine(BackupPathSrc, reldirpath.Substring(1));

                // First handle the directory changes (new/deleted already set so either MetadataChnaged or Unchanged)
                FileMetadata curnode = new FileMetadata(absolutedirpath);
                if (deltanode.DirMetadata.Changes == null) // Is unset?
                {
                    FileMetadata.FileStatus status = curnode.DirectoryDifference(deltanode.DirMetadata);
                    if (status == FileMetadata.FileStatus.Unchanged)
                    {
                        deltanode.DirMetadata.Changes = (status, null);
                    }
                    else
                    {
                        deltanode.DirMetadata.Changes = (status, curnode);
                    }
                }

                // Now handle files
                List<string> fsfiles = null;
                try
                {
                    fsfiles = new List<string>(Directory.GetFiles(absolutedirpath).Select((string filepath) => Path.GetFileName(filepath)));
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
                List<string> deltafiles = new List<string>(deltanode.Files.Keys);
                deltafiles.Sort();
                fsfiles.Sort();

                int deltaidx = 0;
                int fsidx = 0;
                while (deltaidx < deltafiles.Count && fsidx < fsfiles.Count)
                {
                    if (deltafiles[deltaidx] == fsfiles[fsidx]) // Names match
                    {
                        string filename = deltafiles[deltaidx];
                        int trackclass = 2;
                        if (trackpatterns != null)
                        {
                            trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                        }
                        try // We (may) read the file's metadata here so wrap errors
                        {
                            FileMetadata deltafm = deltanode.Files[filename];
                            FileMetadata curfm = new FileMetadata(Path.Combine(absolutedirpath, filename));
                            switch (trackclass)
                            {
                                case 0:
                                    // file should be ignored completely, but is in the old tree
                                    // aka the track pattern changed and the file should be marked as "deleted"
                                    deltafm.Changes = (FileMetadata.FileStatus.Deleted, null);
                                    break;
                                case 1: // Dont scan if we have a previous version
                                    if (curfm.FileDifference(deltanode.DirMetadata))
                                    {
                                        deltafm.Changes = (FileMetadata.FileStatus.MetadataChange, curfm);
                                    }
                                    else
                                    {
                                        deltanode.DirMetadata.Changes = (FileMetadata.FileStatus.Unchanged, null);
                                    }
                                    break;
                                case 2: // Dont scan if we have a previous version and its metadata indicates no change
                                    // If file size and datemodified unchanged we assume no change
                                    if (deltafm.FileSize == curfm.FileSize && deltafm.DateModifiedUTC == curfm.DateModifiedUTC)
                                    {
                                        // Still update metadata if necessary (ie dateaccessed changed)
                                        if (curfm.FileDifference(deltafm))
                                        {
                                            deltafm.Changes = (FileMetadata.FileStatus.MetadataChange, curfm);
                                        }
                                        else
                                        {
                                            deltafm.Changes = (FileMetadata.FileStatus.Unchanged, null);
                                        }
                                    }
                                    else // May have been a change to data
                                    {
                                        deltafm.Changes = (FileMetadata.FileStatus.DataModified, curfm);
                                    }
                                    break;
                                case 3: // Scan file
                                    deltafm.Changes = (FileMetadata.FileStatus.DataModified, curfm);
                                    break;
                                default:
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        deltaidx++;
                        fsidx++;
                    }
                    else if (deltafiles[deltaidx].CompareTo(fsfiles[fsidx]) < 0) // deltafiles[deltaidx] earlier in alphabet than fsfiles[fsidx]
                    {
                        // File in old tree but no longer in filesystem
                        try
                        {
                            FileMetadata deltafm = deltanode.Files[deltafiles[deltaidx]];
                            deltafm.Changes = (FileMetadata.FileStatus.Deleted, null);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        deltaidx++;
                    }
                    else // deltafiles[deltaidx] later in alphabet than fsfiles[fsidx]
                    {
                        string filename = fsfiles[fsidx];
                        int trackclass = 2;
                        if (trackpatterns != null)
                        {
                            trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                        }
                        // File on filesystem not in old tree
                        try
                        {
                            switch (trackclass)
                            {
                                case 0: // dont add if untracked
                                    break;
                                default:
                                    FileMetadata curfm = new FileMetadata(Path.Combine(absolutedirpath, filename))
                                    {
                                        Changes = (FileMetadata.FileStatus.New, null)
                                    };
                                    deltanode.AddFile(curfm);
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                        fsidx++;
                    }
                }
                for (; deltaidx < deltafiles.Count; deltaidx++)
                {
                    // File in old tree but no longer in filesystem
                    try
                    {
                        FileMetadata deltafm = deltanode.Files[deltafiles[deltaidx]];
                        deltafm.Changes = (FileMetadata.FileStatus.Deleted, null);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
                for (; fsidx < fsfiles.Count; fsidx++)
                {
                    string filename = fsfiles[fsidx];
                    int trackclass = 2;
                    if (trackpatterns != null)
                    {
                        trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                    }
                    // File on filesystem not in old tree
                    try
                    {
                        switch (trackclass)
                        {
                            case 0: // dont add if untracked
                                break;
                            default:
                                FileMetadata curfm = new FileMetadata(Path.Combine(absolutedirpath, fsfiles[fsidx]))
                                {
                                    Changes = (FileMetadata.FileStatus.New, null)
                                };
                                deltanode.AddFile(curfm);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }

                List<string> fssubdirs;
                try
                {
                    // Use GetFileName because GetDirectories doesnt return trailing backslashes, so GetDirectoryName will return the partent directory
                    fssubdirs = new List<string>(Directory.GetDirectories(absolutedirpath).Select((string dirpath) => Path.GetFileName(dirpath)));
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
                List<string> deltasubdirs = new List<string>(deltanode.Directories.Keys);
                deltasubdirs.Sort();
                fssubdirs.Sort();

                deltaidx = 0;
                fsidx = 0;
                while (deltaidx < deltasubdirs.Count && fsidx < fsfiles.Count)
                {
                    if (deltasubdirs[deltaidx] == fssubdirs[fsidx]) // Names match
                    {
                        string dirname = fssubdirs[fsidx];
                        deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), deltanode.Directories[dirname]));
                        deltaidx++;
                        fsidx++;
                    }
                    else if (deltasubdirs[deltaidx].CompareTo(fssubdirs[fsidx]) < 0) // deltasubdirs[deltaidx] earlier in alphabet than fssubdirs[fsidx]
                    {
                        // Directory in oldmtree not but no longer in filesystem
                        deltanode.Directories[deltasubdirs[deltaidx]].DirMetadata.Changes = (FileMetadata.FileStatus.Deleted, null);
                        // Dont queue because deleted
                        deltaidx++;
                    }
                    else
                    {
                        // Directory in filesystem not in old tree
                        FileMetadata dirmeta = new FileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]))
                        {
                            Changes = (FileMetadata.FileStatus.New, null)
                        };
                        deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), deltanode.AddDirectory(dirmeta)));
                        fsidx++;
                    }
                }
                for (; deltaidx < deltasubdirs.Count; deltaidx++)
                {
                    // Directory in oldmtree not but no longer in filesystem
                    deltanode.Directories[deltasubdirs[deltaidx]].DirMetadata.Changes = (FileMetadata.FileStatus.Deleted, null);
                    // Dont queue because deleted
                }
                for (; fsidx < fssubdirs.Count; fsidx++)
                {
                    // Directory in filesystem not in old tree
                    FileMetadata dirmeta = new FileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]))
                    {
                        Changes = (FileMetadata.FileStatus.New, null)
                    };
                    MetadataNode newnode = deltanode.AddDirectory(dirmeta);
                    deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), newnode));
                }
            }
            return deltamtree;
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
        public void RunBackup(string backupsetname, string message, bool async=true, bool differentialbackup=true, bool usetrackclasses=true, 
            List<Tuple<int, string>> trackpatterns=null, string prev_backup_hash_prefix=null)
        {
            MetadataNode deltatree = null;

            if (usetrackclasses)
            {
                try
                {
                    trackpatterns = GetTrackClasses();
                }
                catch (Exception)
                {
                    trackpatterns = null;
                }
            }

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    previousbackup = DefaultBackups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = DefaultBackups.GetBackupRecord(backupsetname);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(DefaultBlobs, previousbackup.MetadataTreeHash);
                    deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, previousmtree);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, null);
            }

            List<Task> backupops = new List<Task>();
            BackupDeltaNode(Path.DirectorySeparatorChar.ToString(), deltatree);
            void BackupDeltaNode(string relpath, MetadataNode parent)
            {
                if (parent.DirMetadata.Changes == null)
                {
                    throw new Exception("Reached metadata without delta");
                }
                var status = parent.DirMetadata.Changes.Value.status;
                if (status != FileMetadata.FileStatus.Deleted)
                {
                    // Not deleted so will handle children
                    if (status == FileMetadata.FileStatus.MetadataChange)
                    {
                        parent.DirMetadata = parent.DirMetadata.Changes.Value.updated;
                    }
                    var files = parent.Files.Keys.ToList();
                    foreach (var file in files)
                    {
                        if (parent.Files[file].Changes == null)
                        {
                            throw new Exception("Reached metadata without delta");
                        }
                        var fstatus = parent.Files[file].Changes.Value.status;
                        // Exchnage for metadata in Changes
                        if (fstatus == FileMetadata.FileStatus.MetadataChange || fstatus == FileMetadata.FileStatus.DataModified)
                        {
                            parent.Files[file] = parent.Files[file].Changes.Value.updated;
                        }
                        // Store file data
                        if (fstatus == FileMetadata.FileStatus.New || fstatus == FileMetadata.FileStatus.DataModified)
                        {
                            if (async)
                            {
                                backupops.Add(Task.Run(() => BackupFileSync(backupsetname, Path.Combine(relpath, file), parent.Files[file])));
                            }
                            else
                            {
                                BackupFileSync(backupsetname, Path.Combine(relpath, file), parent.Files[file]);
                            }
                        }
                    }
                    foreach (var dir in parent.Directories.Values)
                    {
                        BackupDeltaNode(Path.Combine(relpath, dir.DirMetadata.FileName) + Path.DirectorySeparatorChar, dir);
                    }
                }
            }
            if (async)
            {
                Task.WaitAll(backupops.ToArray());
            }

            /*
            // Add new metadatatree to metastore
            byte[] newmtreebytes = newmetatree.serialize();
            //byte[] newmtreehash = Blobs.StoreDataAsync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            byte[] newmtreehash = Blobs.StoreDataSync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);
            */

            byte[] newmtreehash = deltatree.Store(DefaultBlobs, backupsetname);

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

        private List<Tuple<int, string>> GetTrackClasses()
        {
            List<Tuple<int, string>> trackclasses = new List<Tuple<int, string>>();
            using (FileStream fs = new FileStream(Path.Combine(BackupPathSrc, ".backuptrack"), FileMode.Open))
            {
                using (StreamReader reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] ctp = line.Split(' ');
                        trackclasses.Add(new Tuple<int, string>(Convert.ToInt32(ctp[0]), ctp[1]));
                    }
                }
            }
            return trackclasses;
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

        /// <summary>
        /// Backup a file into the given metadatanode
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        protected void BackupFileSync(string backupset, string relpath, FileMetadata fileMetadata)
        {
            try
            {
                if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    relpath = relpath.Substring(1);
                }
                FileStream readerbuffer = File.OpenRead(Path.Combine(BackupPathSrc, relpath));
                byte[] filehash = DefaultBlobs.StoreDataSync(backupset, readerbuffer, BlobLocation.BlobTypes.FileBlob);
                fileMetadata.FileHash = filehash;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
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
                DefaultBlobs.TransferBackup(dstblobs, backupsetname, backup.Item1, includefiles & !backup.Item2);
            }
            dstblobs.SaveToDisk();
        }
    }
}
