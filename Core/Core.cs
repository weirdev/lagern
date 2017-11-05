﻿using System;
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
        public ICoreDependencies Dependencies { get; set; }

        public static readonly string CacheSuffix = "~cache";
        
        public static readonly string ShallowSuffix = "~shallow";

        public static readonly string BlobListCacheSuffix = "~bloblistcache" + ShallowSuffix;
        
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
        public Core(string src, string dst, string cache=null) : this(new FSCoreDependencies(src, dst, cache))
        {   }
        

        public static Core LoadCore(ICoreDependencies dependencies)
        {
            dependencies.LoadDstAndCache();
            return new Core(dependencies);
        }

        private Core(ICoreDependencies dependencies)
        {
            Dependencies = dependencies;
        }
        
        public static Core InitializeNew(ICoreDependencies dependencies, string bsname)
        {
            dependencies.InitializeNewDstAndCache(bsname);
            return new Core(dependencies);
        }

        public List<(string path, FileMetadata.FileStatus change)> GetWTStatus(string backupsetname, bool differentialbackup = true,
            List<(int trackclass, string pattern)> trackpatterns = null, string prev_backup_hash_prefix = null)
        {
            if (!Dependencies.DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            MetadataNode deltatree = null;

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    previousbackup = Dependencies.DefaultBackups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = Dependencies.DefaultBackups.GetBackupRecord(backupsetname);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(Dependencies.DefaultBlobs, previousbackup.MetadataTreeHash);
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
        private MetadataNode GetDeltaMetadataTree(string backupsetname, List<(int trackclass, string pattern)> trackpatterns = null,
            MetadataNode previousmtree=null) // TODO: Add detailed information about nature of differences with prev backup
        {
            if (!Dependencies.DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            MetadataNode deltamtree = previousmtree;
            Queue<(string path, MetadataNode node)> deltamnodequeue = new Queue<(string path, MetadataNode node)>();
            if (deltamtree != null)
            {
                // We always assume the matching of deltatree root to fs backup root is valid
                // So make the name equal, and set status to metadatachange if they were different
                FileMetadata dirmetadata = Dependencies.GetFileMetadata("");
                if (deltamtree.DirMetadata.FileName != dirmetadata.FileName)
                {
                    deltamtree.DirMetadata.Changes = (FileMetadata.FileStatus.MetadataChange, dirmetadata);
                }
                deltamnodequeue.Enqueue((Path.DirectorySeparatorChar.ToString(), deltamtree));
            }
            else
            {
                deltamtree = new MetadataNode(Dependencies.GetFileMetadata(""), null);
                deltamnodequeue.Enqueue((Path.DirectorySeparatorChar.ToString(), deltamtree));
            }
            while (deltamnodequeue.Count > 0)
            {
                (string reldirpath, MetadataNode deltanode) = deltamnodequeue.Dequeue();

                // First handle the directory changes (new/deleted already set so either MetadataChnaged or Unchanged)
                FileMetadata curnode = Dependencies.GetFileMetadata(reldirpath);
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
                    fsfiles = new List<string>(Dependencies.GetDirectoryFiles(reldirpath));
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
                            FileMetadata curfm = Dependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
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
                                    FileMetadata curfm = Dependencies.GetFileMetadata(Path.Combine(reldirpath, filename));
                                    curfm.Changes = (FileMetadata.FileStatus.New, null);
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
                                FileMetadata curfm = Dependencies.GetFileMetadata(Path.Combine(reldirpath, fsfiles[fsidx]));
                                curfm.Changes = (FileMetadata.FileStatus.New, null);
                                deltanode.AddFile(curfm);
                                break;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }

                // Handle directories
                List<string> fssubdirs;
                try
                {
                    // Use GetFileName because GetDirectories doesnt return trailing backslashes, so GetDirectoryName will return the partent directory
                    fssubdirs = new List<string>(Dependencies.GetDirectoryFiles(reldirpath));
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
                        if (CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                        {
                            deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), deltanode.Directories[dirname]));
                        }
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
                        if (CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                        {
                            deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), deltanode.AddDirectory(dirmeta)));
                        }
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
                    if (CheckTrackAnyDirectoryChild(Path.Combine(reldirpath, fssubdirs[fsidx]), trackpatterns))
                    {
                        deltamnodequeue.Enqueue((Path.Combine(reldirpath, fssubdirs[fsidx]), newnode));
                    }
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
        public void RunBackup(string backupsetname, string message, bool async=true, bool differentialbackup=true, 
            List<(int trackclass, string pattern)> trackpatterns=null, string prev_backup_hash_prefix=null)
        {
            if (!Dependencies.DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            MetadataNode deltatree = null;

            if (differentialbackup)
            {
                BackupRecord previousbackup;
                try
                {
                    previousbackup = Dependencies.DefaultBackups.GetBackupRecord(backupsetname, prev_backup_hash_prefix);
                }
                catch
                {
                    try
                    {
                        previousbackup = Dependencies.DefaultBackups.GetBackupRecord(backupsetname);
                    }
                    catch
                    {
                        previousbackup = null;
                    }
                }
                if (previousbackup != null)
                {
                    MetadataNode previousmtree = MetadataNode.Load(Dependencies.DefaultBlobs, previousbackup.MetadataTreeHash);
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

            byte[] newmtreehash = deltatree.Store(Dependencies.DefaultBlobs, backupsetname);

            Dependencies.DefaultBackups.AddBackup(backupsetname, message, newmtreehash, false);

            SyncCache(backupsetname);
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
                Dependencies.SaveDefaultBlobStoreIndex();
                if (Dependencies.CacheBlobs != null && Dependencies.DestinationAvailable)
                {
                    Dependencies.SaveCacheBlobStoreIndex();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }
        
        public void SyncCache(string backupsetname)
        {
            if (Dependencies.CacheBackups != null)
            {
                if (Dependencies.DestinationAvailable)
                {
                    Dependencies.DefaultBackups.SyncCache(Dependencies.CacheBackups, backupsetname);
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
            if (!Dependencies.DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            try
            {
                MetadataNode mtree = MetadataNode.Load(Dependencies.DefaultBlobs, Dependencies.DefaultBackups.GetBackupRecord(backupsetname, backuphashprefix).MetadataTreeHash);
                FileMetadata filemeta = mtree.GetFile(relfilepath);
                if (filemeta != null)
                {
                    byte[] filedata = Dependencies.DefaultBlobs.RetrieveData(filemeta.FileHash);
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
                if (!Dependencies.DestinationAvailable)
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
        public (int allreferencesizes, int uniquereferencesizes) GetBackupSizes(string backuphashstring)
        {
            return Dependencies.DefaultBlobs.GetSizes(HashTools.HexStringToByteArray(backuphashstring));
        }

        /// <summary>
        /// Backup a directory to the given metadatanode
        /// </summary>
        /// <param name="relpath"></param>
        /// <param name="mtree"></param>
        private void BackupDirectory(string relpath, MetadataNode mtree)
        {
            mtree.AddDirectory(Path.GetDirectoryName(relpath), Dependencies.GetFileMetadata(relpath));
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
                FileStream readerbuffer = Dependencies.GetFileData(relpath);
                byte[] filehash = Dependencies.DefaultBlobs.StoreData(backupset, readerbuffer, BlobLocation.BlobTypes.FileBlob);
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
        public (IEnumerable<(string backuphash, DateTime backuptime, string message)>, bool chache) GetBackups(string backupsetname)
        {// TODO: does this need to exist here
            if (!Dependencies.DestinationAvailable)
            {
                backupsetname = backupsetname + CacheSuffix;
            }

            List<(string, DateTime, string)> backups = new List<(string, DateTime, string)>();
            foreach (var backup in Dependencies.DefaultBackups.LoadBackupSet(backupsetname).Backups)
            {
                var br = Dependencies.DefaultBackups.GetBackupRecord(backupsetname, backup.hash);
                backups.Add((HashTools.ByteArrayToHexViaLookup32(backup.hash).ToLower(),
                    br.BackupTime, br.BackupMessage));
            }
            return (backups, !Dependencies.DestinationAvailable);
        }

        /// <summary>
        /// Remove a backup from the BackupStore and its data from the BlobStore.
        /// </summary>
        /// <param name="backupsetname"></param>
        /// <param name="backuphashprefix"></param>
        public void RemoveBackup(string backupsetname, string backuphashprefix)
        {
            Dependencies.DefaultBackups.RemoveBackup(backupsetname, backuphashprefix);
            // TODO: remove from cache or require sync?
            SaveBlobIndices();
        }

        /// <summary>
        /// Transfer a backupset and its data to a new location.
        /// </summary>
        /// <param name="src">The Core containing the backup store (and backing blobstore) to be transferred.</param>
        /// <param name="dst">The lagern directory you wish to transfer to.</param>
        public void TransferBackupSet(string backupsetname, ICoreDependencies dstDependencies, bool includefiles)
        {
            Core dstCore = InitializeNew(dstDependencies, backupsetname);
            BackupSet backupSet = Dependencies.DefaultBackups.LoadBackupSet(backupsetname);
            // Transfer backup set
            dstCore.Dependencies.DefaultBackups.SaveBackupSet(backupSet, backupsetname);
            // Transfer backing data
            foreach (var backup in backupSet.Backups)
            {
                Dependencies.DefaultBlobs.TransferBackup(dstCore.Dependencies.DefaultBlobs, backupsetname, backup.hash, includefiles & !backup.shallow);
            }
            dstCore.Dependencies.SaveDefaultBlobStoreIndex();
        }
    }
}
