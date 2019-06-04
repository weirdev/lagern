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
        /// True if the regular backup destination is available.
        /// If the destination is not available we attempt to use the cache.
        /// </summary>
        public bool DestinationAvailable { get; set; }

        public ICoreSrcDependencies SrcDependencies { get; set; }
        // TODO: Rename to DstsDependencies
        public List<ICoreDstDependencies> DefaultDstDependencies { get; set; }
        public ICoreDstDependencies CacheDependencies { get; set; }
        
        public static readonly string CacheSuffix = "~cache";
        
        public static readonly string ShallowSuffix = "~shallow";

        public static readonly string BlobListCacheSuffix = "~bloblistcache" + ShallowSuffix;

        public static readonly string BackupBlobIndexFile = "hashindex";

        public static readonly string SettingsFilename = ".settings";

        public Core(ICoreSrcDependencies src, ICoreDstDependencies dst, ICoreDstDependencies cache = null)
        {
            SrcDependencies = src;
            if (dst == null)
            {
                DestinationAvailable = false;
                if (cache != null)
                {
                    DefaultDstDependencies = new List<ICoreDstDependencies>(1) { cache };
                }
                else
                {
                    throw new ArgumentNullException("Dst and cache are null, cannot initialize");
                }
            }
            else
            {
                DestinationAvailable = true;
                DefaultDstDependencies = new List<ICoreDstDependencies>(1) { dst };
            }
            CacheDependencies = cache;
        }
        
        /// <summary>
        /// Loads and existing lagern Core. Core surfaces all public methods for use by programs implementing this library.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="src">Backup source directory</param>
        /// <param name="dst">Backup destination directory</param>
        public static Core LoadDiskCore(string src, string dst, string cache=null, string password=null)
        {
            FSCoreSrcDependencies srcdep = FSCoreSrcDependencies.Load(src, new DiskFSInterop());
            CoreDstDependencies dstdep;
            try
            {
                dstdep = CoreDstDependencies.Load(DiskDstFSInterop.Load(dst, password), cache != null);
            }
            catch (Exception)
            {
                dstdep = null;
            }
            CoreDstDependencies cachedep = null;
            if (cache != null)
            {
                cachedep = CoreDstDependencies.Load(DiskDstFSInterop.Load(cache));
            }
            return new Core(srcdep, dstdep, cachedep);
        }

        // TODO: This method shouldn't exist in Core, it should be in its own class of similar helper methods,
        // or simply not exist at all
        public static Core InitializeNewDiskCore(string bsname, string src, string dst, string cache = null, string password=null)
        {
            ICoreSrcDependencies srcdep = FSCoreSrcDependencies.InitializeNew(bsname, src, new DiskFSInterop(), dst, cache, null, password!=null);
            CoreDstDependencies dstdep = CoreDstDependencies.InitializeNew(bsname, DiskDstFSInterop.InitializeNew(dst, password), cache!=null);
            CoreDstDependencies cachedep = null;
            if (cache != null)
            {
                cachedep = CoreDstDependencies.InitializeNew(bsname + CacheSuffix, DiskDstFSInterop.InitializeNew(cache), false);
            }
            return new Core(srcdep, dstdep, cachedep);
        }

        /// <summary>
        /// Gets list of changes, relative to at most one previous backup
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="differentialbackup"></param>
        /// <param name="trackpatterns"></param>
        /// <param name="prev_backup_hash_prefix"></param>
        /// <returns></returns>
        public List<(string path, FileMetadata.FileStatus change)> GetWTStatus(string backupsetname, bool differentialbackup = true,
            List<(int trackclass, string pattern)> trackpatterns = null, string prev_backup_hash_prefix = null)
        {
            if (!DestinationAvailable)
            {
                backupsetname += CacheSuffix;
            }

            MetadataNode deltatree = null;

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    // Assume all destinations have the same most recent backup, so just use the first backup
                    previousbackup = DefaultDstDependencies[0].Backups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = DefaultDstDependencies[0].Backups.GetBackupRecord(backupsetname);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(DefaultDstDependencies[0].Blobs, 
                        previousbackup.MetadataTreeHash);
                    deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, new List<MetadataNode>() { previousmtree })[0];
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                deltatree = GetDeltaMetadataTree(backupsetname, trackpatterns, null)[0];
            }
            List<(string path, FileMetadata.FileStatus change)> changes = new List<(string path, FileMetadata.FileStatus change)>();
            GetDeltaNodeChanges(Path.DirectorySeparatorChar.ToString(), deltatree);

            return changes;

            // Recursive helper function to traverse delta tree
            void GetDeltaNodeChanges(string relpath, MetadataNode node)
            {
                if (node.DirMetadata.Status == null)
                {
                    throw new Exception("Reached metadata without delta");
                }
                var status = node.DirMetadata.Status;
                if (status != null)
                {
                    var valstatus = status.Value;
                    if (status == FileMetadata.FileStatus.Deleted)
                    {
                        changes.Add((relpath, valstatus));
                        // If deleted dont handle children
                    }
                    else
                    {
                        // Not deleted so will handle children
                        if (status != FileMetadata.FileStatus.Unchanged)
                        {
                            changes.Add((relpath, valstatus));
                        }
                        foreach (var file in node.Files.Values)
                        {
                            if (file.Status == null)
                            {
                                throw new Exception("Reached metadata without delta");
                            }
                            var fstatus = file.Status;
                            if (fstatus != null)
                            {
                                var valfstatus = fstatus.Value;
                                if (fstatus != FileMetadata.FileStatus.Unchanged)
                                {
                                    changes.Add((Path.Combine(relpath, file.FileName), valfstatus));
                                }
                            }
                        }
                        foreach (var dir in node.Directories.Values)
                        {
                            GetDeltaNodeChanges(Path.Combine(relpath, dir.DirMetadata.FileName) + Path.DirectorySeparatorChar, dir);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the working tree status
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="trackpatterns"></param>
        /// <param name="previousmtrees"></param>
        /// <returns>A delta tree mapping </returns>
        private List<MetadataNode> GetDeltaMetadataTree(string backupsetname, List<(int trackclass, string pattern)> trackpatterns = null,
            List<MetadataNode> previousmtrees=null)
        {
            if (!DestinationAvailable)
            {
                backupsetname += CacheSuffix;
            }

            Queue<string> deltamnodequeue = new Queue<string>();
            FileMetadata rootdirmetadata = SrcDependencies.GetFileMetadata("");

            // Non differential backup equivalent to differential backup to single destination without a previous tree
            if (previousmtrees == null)
            {
                previousmtrees = new List<MetadataNode>() { null };
            }

            List<MetadataNode> deltamtrees = previousmtrees.Select((_) => new MetadataNode(rootdirmetadata, null)).ToList();

            foreach (var (previousmtree, deltamtree) in previousmtrees.Zip(deltamtrees, (p, d) => (p, d)))
            {
                if (previousmtree != null)
                {
                    // We always assume the matching of deltatree root to fs backup root is valid
                    // So make the name equal, and set status to metadatachange if they were different
                    if (previousmtree.DirMetadata.FileName != rootdirmetadata.FileName)
                    {
                        deltamtree.DirMetadata.Status = FileMetadata.FileStatus.MetadataChange;
                    }
                    else
                    {
                        deltamtree.DirMetadata.Status = FileMetadata.FileStatus.Unchanged;
                    }
                }
                else
                {
                    deltamtree.DirMetadata.Status = FileMetadata.FileStatus.New;
                }
            }

            deltamnodequeue.Enqueue(Path.DirectorySeparatorChar.ToString());

            while (deltamnodequeue.Count > 0)
            {
                string reldirpath = deltamnodequeue.Dequeue();
                List<MetadataNode> deltanodes = deltamtrees.Select((dmt) => dmt.GetDirectory(reldirpath)).ToList();
                List<MetadataNode> previousmnodes = previousmtrees.Select((mt) => mt?.GetDirectory(reldirpath)).ToList();

                // Null delta nodes indicate that a directory is not to be backed up for that backup,
                // so we exclude the deltanode and corresponding previousmnode
                List<MetadataNode> filtereddn = new List<MetadataNode>();
                List<MetadataNode> filteredpn = new List<MetadataNode>();
                for (int i = 0; i < deltanodes.Count; i++)
                {
                    if (deltanodes[i] != null)
                    {
                        filtereddn.Add(deltanodes[i]);
                        filteredpn.Add(deltanodes[i]);
                    }
                }
                deltanodes = filtereddn;
                previousmnodes = filteredpn;

                // Now handle files
                List<string> fsfiles = null;
                try
                {
                    fsfiles = new List<string>(SrcDependencies.GetDirectoryFiles(reldirpath));
                }
                catch (UnauthorizedAccessException e) // TODO: More user friendly output here
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

                // Used this slightly ackward cache pattern to more easily efficiently handle per-destination tracking classes in a future release
                Dictionary<string, FileMetadata> filemetadatacache = new Dictionary<string, FileMetadata>();

                for (int prevmnidx = 0; prevmnidx < previousmnodes.Count; prevmnidx++)
                {
                    var previousmnode = previousmnodes[prevmnidx];
                    var deltamnode = deltanodes[prevmnidx];
                    List<string> previousfiles;
                    if (previousmnode != null)
                    {
                        previousfiles = new List<string>(previousmnode.Files.Keys);
                    } else
                    {
                        previousfiles = new List<string>(0);
                    }
                    previousfiles.Sort();
                    fsfiles.Sort();

                    int previdx = 0;
                    int fsidx = 0;
                    while (previdx < previousfiles.Count && fsidx < fsfiles.Count)
                    {
                        if (previousfiles[previdx] == fsfiles[fsidx]) // Names match
                        {
                            string filename = previousfiles[previdx];
                            int trackclass = 2; // TODO: make this an application wide constant
                            if (trackpatterns != null)
                            {
                                trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                            }
                            try // We (may) read the file's metadata here so wrap errors
                            {
                                if (trackclass != 0)
                                {
                                    FileMetadata prevfm = previousmnode.Files[filename];
                                    FileMetadata curfm;
                                    if (filemetadatacache.ContainsKey(filename))
                                    {
                                        curfm = filemetadatacache[filename];
                                    } else {
                                        curfm = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                        filemetadatacache[filename] = curfm;
                                    }
                                    // Create a copy FileMetada to hold the changes
                                    curfm = new FileMetadata(curfm);

                                    switch (trackclass)
                                    {
                                        case 1: // Dont scan if we have a previous version
                                            if (curfm.FileDifference(prevfm))
                                            {
                                                curfm.Status = FileMetadata.FileStatus.MetadataChange;
                                            }
                                            else
                                            {
                                                curfm.Status = FileMetadata.FileStatus.Unchanged;
                                            }
                                            break;
                                        case 2: // Dont scan if we have a previous version and its metadata indicates no change
                                                // If file size and datemodified unchanged we assume no change
                                            if (prevfm.FileSize == curfm.FileSize && prevfm.DateModifiedUTC == curfm.DateModifiedUTC)
                                            {
                                                // Still update metadata if necessary (ie dateaccessed changed)
                                                if (curfm.FileDifference(prevfm))
                                                {
                                                    curfm.Status = FileMetadata.FileStatus.MetadataChange;
                                                }
                                                else
                                                {
                                                    curfm.Status = FileMetadata.FileStatus.Unchanged;
                                                }
                                            }
                                            else // May have been a change to data
                                            {
                                                curfm.Status = FileMetadata.FileStatus.DataModified;
                                            }
                                            break;
                                        case 3: // Scan file
                                            curfm.Status = FileMetadata.FileStatus.DataModified;
                                            break;
                                        default:
                                            break;
                                    }
                                    deltamnode.AddFile(curfm);
                                }
                                else // file exists in previous, but now has tracking class 0, thus is effectively deleted
                                {
                                    FileMetadata prevfm = previousmnode.Files[filename];
                                    prevfm = new FileMetadata(prevfm)
                                    {
                                        Status = FileMetadata.FileStatus.Deleted
                                    };
                                    deltamnode.AddFile(prevfm);
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.Message);
                            }
                            previdx++;
                            fsidx++;
                        }
                        else if (previousfiles[previdx].CompareTo(fsfiles[fsidx]) < 0) // deltafiles[deltaidx] earlier in alphabet than fsfiles[fsidx]
                        {
                            // File in old tree but no longer in filesystem
                            string filename = previousfiles[previdx];
                            FileMetadata prevfm = previousmnode.Files[filename];
                            prevfm = new FileMetadata(prevfm)
                            {
                                Status = FileMetadata.FileStatus.Deleted
                            };
                            deltamnode.AddFile(prevfm);
                            previdx++;
                        }
                        else // deltafiles[deltaidx] later in alphabet than fsfiles[fsidx]
                        {
                            // File on filesystem not in old tree
                            string filename = fsfiles[fsidx];
                            int trackclass = 2;
                            if (trackpatterns != null)
                            {
                                trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                            }

                            try
                            {
                                switch (trackclass)
                                {
                                    case 0: // dont add if untracked
                                        break;
                                    default:
                                        FileMetadata curfm;
                                        if (filemetadatacache.ContainsKey(filename))
                                        {
                                            curfm = filemetadatacache[filename];
                                        }
                                        else
                                        {
                                            curfm = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                            filemetadatacache[filename] = curfm;
                                        }
                                        curfm = new FileMetadata(curfm)
                                        {
                                            Status = FileMetadata.FileStatus.New
                                        };
                                        deltamnode.AddFile(curfm);
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
                    for (; previdx < previousfiles.Count; previdx++)
                    {
                        // File in old tree but no longer in filesystem
                        string filename = previousfiles[previdx];
                        FileMetadata prevfm = previousmnode.Files[filename];
                        prevfm = new FileMetadata(prevfm)
                        {
                            Status = FileMetadata.FileStatus.Deleted
                        };
                        deltamnode.AddFile(prevfm);
                    }
                    for (; fsidx < fsfiles.Count; fsidx++)
                    {
                        // File on filesystem not in old tree
                        string filename = fsfiles[fsidx];
                        int trackclass = 2;
                        if (trackpatterns != null)
                        {
                            trackclass = FileTrackClass(Path.Combine(reldirpath.Substring(1), filename), trackpatterns);
                        }
                        try
                        {
                            switch (trackclass)
                            {
                                case 0: // dont add if untracked
                                    break;
                                default:
                                    FileMetadata curfm;
                                    if (filemetadatacache.ContainsKey(filename))
                                    {
                                        curfm = filemetadatacache[filename];
                                    }
                                    else
                                    {
                                        curfm = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                        filemetadatacache[filename] = curfm;
                                    }
                                    curfm = new FileMetadata(curfm)
                                    {
                                        Status = FileMetadata.FileStatus.New
                                    };
                                    deltamnode.AddFile(curfm);
                                    break;
                            }
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e.Message);
                        }
                    }
                }


                // Handle directories
                List<string> fssubdirs;
                try
                {
                    // Use GetFileName because GetDirectories doesnt return trailing backslashes, so GetDirectoryName will return the partent directory
                    fssubdirs = new List<string>(SrcDependencies.GetSubDirectories(reldirpath));
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

                Dictionary<string, FileMetadata> dirmetadatacache = new Dictionary<string, FileMetadata>();

                HashSet<string> dirsToQueue = new HashSet<string>();

                for (int prevmnidx = 0; prevmnidx < previousmnodes.Count; prevmnidx++)
                {
                    var previousmnode = previousmnodes[prevmnidx];
                    var deltamnode = deltanodes[prevmnidx];

                    List<string> previoussubdirs;
                    if (previousmnode != null)
                    {
                        previoussubdirs = new List<string>(previousmnode.Directories.Keys);
                    }
                    else
                    {
                        previoussubdirs = new List<string>(0);
                    }
                    previoussubdirs.Sort();
                    fssubdirs.Sort();


                    int previdx = 0;
                    int fsidx = 0;
                    while (previdx < previoussubdirs.Count && fsidx < fssubdirs.Count)
                    {
                        if (previoussubdirs[previdx] == fssubdirs[fsidx]) // Names match
                        {
                            string dirname = fssubdirs[fsidx];
                            if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, dirname), trackpatterns))
                            {
                                FileMetadata fssubdirmetadata;
                                if (dirmetadatacache.ContainsKey(dirname))
                                {
                                    fssubdirmetadata = dirmetadatacache[dirname];
                                }
                                else
                                {
                                    fssubdirmetadata = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                    dirmetadatacache[dirname] = fssubdirmetadata;
                                }
                                FileMetadata previousdirmetadata = previousmnode.Directories[dirname].DirMetadata;
                                FileMetadata.FileStatus status = fssubdirmetadata.DirectoryDifference(previousdirmetadata);
                                fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                                {
                                    Status = status
                                };
                                deltamnode.AddDirectory(fssubdirmetadata);
                                dirsToQueue.Add(dirname);
                            }
                            else // We are no longer tracking this directory's children so it has been effectively deleted
                            {
                                FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                                if (!dirmetadatacache.ContainsKey(dirname))
                                {
                                    dirmetadatacache[dirname] = prevfm;
                                }
                                prevfm = new FileMetadata(prevfm)
                                {
                                    Status = FileMetadata.FileStatus.Deleted
                                };
                                deltamnode.AddDirectory(prevfm);
                            }
                            previdx++;
                            fsidx++;
                        }
                        else if (previoussubdirs[previdx].CompareTo(fssubdirs[fsidx]) < 0) // deltasubdirs[deltaidx] earlier in alphabet than fssubdirs[fsidx]
                        {
                            // Directory in oldmtree not but no longer in filesystem
                            string dirname = previoussubdirs[previdx];
                            FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                            if (!dirmetadatacache.ContainsKey(dirname))
                            {
                                dirmetadatacache[dirname] = prevfm;
                            }
                            prevfm = new FileMetadata(prevfm)
                            {
                                Status = FileMetadata.FileStatus.Deleted
                            };
                            deltamnode.AddDirectory(prevfm);
                            // Dont queue because deleted
                            previdx++;
                        }
                        else
                        {
                            // Directory in filesystem not in old tree
                            if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                            {
                                string dirname = fssubdirs[fsidx];
                                FileMetadata fssubdirmetadata;
                                if (dirmetadatacache.ContainsKey(dirname))
                                {
                                    fssubdirmetadata = dirmetadatacache[dirname];
                                }
                                else
                                {
                                    fssubdirmetadata = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                    dirmetadatacache[dirname] = fssubdirmetadata;
                                }
                                fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                                {
                                    Status = FileMetadata.FileStatus.New
                                };
                                deltamnode.AddDirectory(fssubdirmetadata);
                                dirsToQueue.Add(dirname);
                            }
                            fsidx++;
                        }
                    }
                    for (; previdx < previoussubdirs.Count; previdx++)
                    {
                        // Directory in oldmtree not but no longer in filesystem
                        string dirname = previoussubdirs[previdx];
                        FileMetadata prevfm = previousmnode.Directories[dirname].DirMetadata;
                        if (!dirmetadatacache.ContainsKey(dirname))
                        {
                            dirmetadatacache[dirname] = prevfm;
                        }
                        prevfm = new FileMetadata(prevfm)
                        {
                            Status = FileMetadata.FileStatus.Deleted
                        };
                        deltamnode.AddDirectory(prevfm);
                        // Dont queue because deleted
                    }
                    for (; fsidx < fssubdirs.Count; fsidx++)
                    {
                        // Directory in filesystem not in old tree
                        if (trackpatterns == null || CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                        {
                            string dirname = fssubdirs[fsidx];
                            FileMetadata fssubdirmetadata;
                            if (dirmetadatacache.ContainsKey(dirname))
                            {
                                fssubdirmetadata = dirmetadatacache[dirname];
                            }
                            else
                            {
                                fssubdirmetadata = SrcDependencies.GetFileMetadata(Path.Combine(reldirpath, fssubdirs[fsidx]));
                                dirmetadatacache[dirname] = fssubdirmetadata;
                            }
                            fssubdirmetadata = new FileMetadata(fssubdirmetadata)
                            {
                                Status = FileMetadata.FileStatus.New
                            };
                            deltamnode.AddDirectory(fssubdirmetadata);
                            dirsToQueue.Add(dirname);
                        }
                    }
                }

                // Record the changes
                foreach (var dirname in dirsToQueue)
                {
                    deltamnodequeue.Enqueue(Path.Combine(reldirpath, dirname));
                }
            }
            return deltamtrees;
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
        /// <returns>The hash of the new backup</returns>
        public byte[] RunBackup(string backupsetname, string message, bool async=true, bool differentialbackup=true, 
            List<(int trackclass, string pattern)> trackpatterns=null, string prev_backup_hash_prefix=null)
        {
            SyncCacheSaveBackupSets(backupsetname);

            if (!DestinationAvailable)
            {
                backupsetname += CacheSuffix;
            }

            List<MetadataNode> deltatrees = null;
            if (differentialbackup)
            {
                List<BackupRecord> previousbackups = new List<BackupRecord>();
                bool no_previous = true;
                foreach (var dst in DefaultDstDependencies)
                {
                    try
                    {
                        BackupRecord previousbackup = dst.Backups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                        if (previousbackup != null)
                        {
                            previousbackups.Add(previousbackup);
                            no_previous = false;
                        } else
                        {
                            throw new KeyNotFoundException();
                        }
                    }
                    catch
                    {
                        BackupRecord previousbackup = dst.Backups.GetBackupRecord(backupsetname);
                        if (previousbackup != null)
                        {
                            // TODO: if user specifies a previous backup hash, we should probably fail if it is not found
                            previousbackups.Add(previousbackup);
                            no_previous = false;
                        }
                        else
                        {
                            previousbackups.Add(null);
                        }
                    }
                }
                if (!no_previous)
                {
                    List<MetadataNode> previousMTrees = new List<MetadataNode>();
                    foreach (var (previousbackup, dst) in previousbackups.Zip(DefaultDstDependencies, (pb, d) => (pb, d)))
                    {
                        previousMTrees.Add(MetadataNode.Load(dst.Blobs,
                            previousbackup.MetadataTreeHash));
                    }
                    deltatrees = GetDeltaMetadataTree(backupsetname, trackpatterns, previousMTrees);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                deltatrees = GetDeltaMetadataTree(backupsetname, trackpatterns, null);
            }

            List<Task> backupops = new List<Task>();
            BackupDeltaNode(Path.DirectorySeparatorChar.ToString(), deltatrees.Zip(Enumerable.Range(0, deltatrees.Count), (t, i) => (i, t)).ToList());

            void BackupDeltaNode(string relpath, List<(int dstidx, MetadataNode node)> indexednodes)
            {
                var changes = indexednodes.Select((idxn) => (idxn.dstidx, idxn.node.DirMetadata.Status)).ToList();
                if (changes.Any((s) => s.Status != FileMetadata.FileStatus.Deleted))
                {
                    // Not deleted so will handle children
                    var allfiles = new Dictionary<string, List<(int dstidx, FileMetadata fileMetadata)>>(); // Filename to destinations that need that file
                    foreach (var (dstidx, node) in indexednodes)
                    {
                        foreach (var filenameandmeta in node.Files)
                        {
                            // Prune out deleted files, they dont affect the backup
                            if (filenameandmeta.Value.Status == FileMetadata.FileStatus.Deleted)
                            {
                                node.Files.TryRemove(filenameandmeta.Key, out _);
                            }
                            else
                            {
                                if (allfiles.ContainsKey(filenameandmeta.Key))
                                {
                                    allfiles[filenameandmeta.Key].Add((dstidx, filenameandmeta.Value));
                                }
                                else
                                {
                                    allfiles[filenameandmeta.Key] = new List<(int, FileMetadata)> { (dstidx, filenameandmeta.Value) };
                                }
                            }
                        }
                    }
                    foreach (var filename in allfiles.Keys)
                    {
                        // Data writes to destinations
                        List<(int dstidx, FileMetadata fileMetadata)> writedestinations = allfiles[filename].Where((difm) =>
                                difm.fileMetadata.Status == FileMetadata.FileStatus.New || difm.fileMetadata.Status == FileMetadata.FileStatus.DataModified)
                            .Select((difm) => (difm.dstidx, difm.fileMetadata)).ToList();

                        if (async)
                        {
                            backupops.Add(Task.Run(() => BackupFileSync(backupsetname, Path.Combine(relpath, filename), writedestinations)));
                        }
                        else
                        {
                            BackupFileSync(backupsetname, Path.Combine(relpath, filename), writedestinations);
                        }                        
                    }

                    var alldirs = new Dictionary<string, List<(int dstidx, MetadataNode metadataNode)>>(); // Filename to destinations that need that file
                    foreach (var (dstidx, node) in indexednodes)
                    {
                        foreach (var dirnameandnode in node.Directories)
                        {
                            // Prune out deleted dirs, they dont affect the backup
                            if (dirnameandnode.Value.DirMetadata.Status == FileMetadata.FileStatus.Deleted)
                            {
                                node.Directories.TryRemove(dirnameandnode.Key, out _);
                            }
                            else
                            {
                                if (alldirs.ContainsKey(dirnameandnode.Key))
                                {
                                    alldirs[dirnameandnode.Key].Add((dstidx, dirnameandnode.Value));
                                }
                                else
                                {
                                    alldirs[dirnameandnode.Key] = new List<(int, MetadataNode)> { (dstidx, dirnameandnode.Value) };
                                }
                            }
                        }
                    }
                    foreach (var dir in alldirs)
                    {
                        BackupDeltaNode(Path.Combine(relpath, dir.Key) + Path.DirectorySeparatorChar, dir.Value);
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

            List<byte[]> newmtreehashes = deltatrees.Select(deltatree => deltatree.Store((byte[] data) => 
                    BlobStore.StoreData(DefaultDstDependencies.Select(dst => dst.Blobs), 
                    backupsetname, data))).ToList();

            byte[] backuphash = null;
            for (int i = 0; i < DefaultDstDependencies.Count; i++)
            {
                var dst = DefaultDstDependencies[i];
                var defaultbset = dst.Backups.LoadBackupSet(backupsetname);
                backuphash = dst.Backups.AddBackup(backupsetname, message, newmtreehashes[i], false, defaultbset);
                dst.Backups.SaveBackupSet(defaultbset, backupsetname);
                // Backup record has just been stored, all data now stored

                // Finalize backup by incrementing reference counts in blobstore as necessary
                dst.Blobs.FinalizeBlobAddition(backupsetname, backuphash, BlobLocation.BlobType.BackupRecord);
            }

            // TODO: Only really need to sync cache with one destination
            SyncCacheSaveBackupSets(backupsetname);

            SaveBlobIndices();
            if (backuphash == null)
            {
                throw new NullReferenceException("backuphash cannot be null after a backup");
            }
            return backuphash;
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
                DefaultDstDependencies.ForEach(x => x.SaveBlobStoreIndex());
                if (CacheDependencies != null && DestinationAvailable)
                {
                    CacheDependencies.SaveBlobStoreIndex();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        // TODO: Return backup sets from DefaultDstDependencies.Backups.SyncCache()
        // then save backup sets in a seperate method
        public void SyncCacheSaveBackupSets(string backupsetname)
        {
            if (CacheDependencies != null && DestinationAvailable)
            {
                BackupSet bset = null;
                BackupSet cachebset = null;
                foreach (var dst in DefaultDstDependencies)
                {
                    (bset, cachebset) = dst.Backups.SyncCache(CacheDependencies.Backups, backupsetname);
                    dst.Backups.SaveBackupSet(bset, backupsetname);
                }
                CacheDependencies.Backups.SaveBackupSet(cachebset, backupsetname + CacheSuffix);
            }
        }

        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public void RestoreFileOrDirectory(string backupsetname, string relfilepath, string restorepath, string backuphashprefix = null, bool absoluterestorepath=false, int backupdst=0)
        {
            if (!DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            try
            {
                var backup = DefaultDstDependencies[backupdst].Backups.GetBackupRecord(backupsetname, backuphashprefix);
                MetadataNode mtree = MetadataNode.Load(DefaultDstDependencies[backupdst].Blobs, backup.MetadataTreeHash);
                FileMetadata filemeta = mtree.GetFile(relfilepath);
                if (filemeta != null)
                {
                    byte[] filedata = DefaultDstDependencies[backupdst].Blobs.RetrieveData(filemeta.FileHash);
                    SrcDependencies.OverwriteOrCreateFile(restorepath, filedata, filemeta, absoluterestorepath);
                }
                else
                {
                    MetadataNode dir = mtree.GetDirectory(relfilepath);
                    if (dir != null)
                    {
                        SrcDependencies.CreateDirectory(restorepath, absoluterestorepath);
                        foreach (var childfile in dir.Files.Values)
                        {
                            RestoreFileOrDirectory(backupsetname, Path.Combine(relfilepath, childfile.FileName), Path.Combine(restorepath, childfile.FileName), backuphashprefix, absoluterestorepath);
                        }
                        foreach (var childdir in dir.Directories.Keys)
                        {
                            RestoreFileOrDirectory(backupsetname, Path.Combine(relfilepath, childdir), Path.Combine(restorepath, childdir), backuphashprefix, absoluterestorepath);
                        }
                        SrcDependencies.WriteOutMetadata(restorepath, dir.DirMetadata, absoluterestorepath); // Set metadata after finished changing contents (postorder)
                    }
                    else
                    {
                        throw new Exception("File/directory to restore not found");
                    }
                }
            }
            catch (Exception e)
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
                throw e;
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
            if (pattern.EndsWith("/"))
            {
                pattern = pattern.Substring(0, pattern.Length - 1);
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
        public static int FileTrackClass(string file, List<(int trackclass, string pattern)> trackpatterns)
        {
            int trackclass = 2;
            foreach (var trackpatter in trackpatterns)
            {
                if (PatternMatchesPath(file, trackpatter.pattern))
                {
                    trackclass = trackpatter.trackclass;
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
        public static bool CheckTrackAnyDirectoryChild(string directory, List<(int trackpattern, string pattern)> trackpatterns)
        {
            bool track = true;
            foreach (var trackpattern in trackpatterns)
            {
                if (track)
                {
                    if (trackpattern.trackpattern == 0)
                    {
                        // Can only exclude if there is a trailing wildcard after 
                        // the rest of the pattern matches to this directory
                        if (trackpattern.pattern[trackpattern.pattern.Length - 1] == '*')
                        {
                            if (trackpattern.pattern.Length == 1) // just "*"
                            {
                                track = false;
                            }
                            else if (PatternMatchesPath(directory, trackpattern.pattern)) 
                            {
                                track = false;
                            }
                        }
                    }
                }
                else
                {
                    if (trackpattern.trackpattern != 0)
                    {
                        int wildpos = trackpattern.pattern.IndexOf('*');
                        if (wildpos == 0)
                        {
                            track = true;
                        }
                        else if (wildpos > 0)
                        {
                            string prefix = trackpattern.pattern.Substring(0, wildpos);
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

        public List<(int trackclass, string pattern)> ReadTrackClassFile(string trackfilepath)
        {
            List<(int, string)> trackclasses = new List<(int, string)>();
            using (FileStream fs = new FileStream(trackfilepath, FileMode.Open))
            {
                using (StreamReader reader = new StreamReader(fs))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        string[] ctp = line.Split(' ');
                        trackclasses.Add((Convert.ToInt32(ctp[0]), ctp[1]));
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
        public (int allreferencesizes, int uniquereferencesizes) GetBackupSizes(string bsname, string backuphashstring, int backupdst=0)
        {
            var br = DefaultDstDependencies[backupdst].Backups.GetBackupRecord(bsname, backuphashstring);
            return DefaultDstDependencies[backupdst].Blobs.GetSizes(br.MetadataTreeHash, BlobLocation.BlobType.MetadataNode);
        }

        /// <summary>
        /// Backup a directory to the given metadatanode
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        private void BackupDirectory(string relpath, MetadataNode mtree)
        {
            mtree.AddDirectory(Path.GetDirectoryName(relpath), SrcDependencies.GetFileMetadata(relpath));
        }

        /// <summary>
        /// Backup a file to all destinations and save its hash to the given filemetadata
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        protected void BackupFileSync(string backupset, string relpath, List<(int dstidx, FileMetadata fileMetadata)> writedestinations)
        {
            try
            {
                if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    relpath = relpath.Substring(1);
                }
                Stream readerbuffer = SrcDependencies.GetFileData(relpath);
                byte[] filehash = BlobStore.StoreData(writedestinations.Select(difm => DefaultDstDependencies[difm.dstidx].Blobs), backupset, readerbuffer);
                foreach (var difm in writedestinations)
                {
                    difm.fileMetadata.FileHash = filehash;
                }
            }
            catch (Exception e)
            {
                throw new IOException($"Failed to backup {relpath}", e);
            }
        }
        
        /// <summary>
        /// Retrieves list of backups from a backupset.
        /// </summary>
        /// <returns>A list of tuples representing the backup times and their associated messages.</returns>
        public (IEnumerable<(string backuphash, DateTime backuptime, string message)> backups, bool cache) GetBackups(string backupsetname, int backupdst=0)
        {
            if (!DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            List<(string, DateTime, string)> backups = new List<(string, DateTime, string)>();
            foreach (var backup in DefaultDstDependencies[backupdst].Backups.LoadBackupSet(backupsetname).Backups)
            {
                var br = DefaultDstDependencies[backupdst].Backups.GetBackupRecord(backupsetname, backup.hash);
                backups.Add((HashTools.ByteArrayToHexViaLookup32(backup.hash).ToLower(),
                    br.BackupTime, br.BackupMessage));
            }
            return (backups, !DestinationAvailable);
        }

        /// <summary>
        /// Remove a backup from the BackupStore and its data from the BlobStore.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="backuphashprefix"></param>
        public void RemoveBackup(string backupsetname, string backuphashprefix, bool forcedelete = false, int backupdst=0)
        {
            SyncCacheSaveBackupSets(backupsetname); // Sync cache first to prevent deletion of data in dst relied on by an unmerged backup in cache
            DefaultDstDependencies[backupdst].Backups.RemoveBackup(backupsetname, backuphashprefix, DestinationAvailable && CacheDependencies==null, forcedelete);
            SyncCacheSaveBackupSets(backupsetname);
            SaveBlobIndices();
        }

        /// <summary>
        /// Transfer a backupset and its data to a new location.
        /// </summary>
        /// <param name="src">The Core containing the backup store (and backing blobstore) to be transferred.</param>
        /// <param name="dst">The lagern directory you wish to transfer to.</param>
        public void TransferBackupSet(string backupsetname, Core dstCore, bool includefiles, int backupdst_transfersrc=0, int backupdst_transferdst=0)
        {
            // TODO: This function probably makes more sense transferring between backup destinations within the current Core object
            BackupSet backupSet = DefaultDstDependencies[backupdst_transfersrc].Backups.LoadBackupSet(backupsetname);
            // Transfer backup set
            dstCore.DefaultDstDependencies[backupdst_transferdst].Backups.SaveBackupSet(backupSet, backupsetname);
            // Transfer backing data
            foreach (var backup in backupSet.Backups)
            {
                DefaultDstDependencies[backupdst_transfersrc].Blobs.TransferBackup(dstCore.DefaultDstDependencies[backupdst_transferdst].Blobs, backupsetname, backup.hash, includefiles & !backup.shallow);
            }
            dstCore.DefaultDstDependencies[backupdst_transferdst].SaveBlobStoreIndex();
        }

        // TODO: Add method for transferring individual backup

        public class BackupRemoveException : Exception
        {
            public BackupRemoveException(string message) : base(message) { }
        }
    }
    
    public enum BackupSetting
    {
        dest,
        cache,
        name,
        cloud_config,
        encryption_enabled
    }

    public enum IndexFileType
    {
        BlobIndex,
        BackupSet,
        SettingsFile,
        EncryptorKeyFile
    }
}
