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
    public class Core
    {
        public string BackuppathSrc { get; set; }

        public string BackuppathDst { get; set; }

        public string BackupIndexDir { get; set; }

        public string HashIndexFile { get; set; }

        public string BackupListFile { get; set; }

        // BlockHashStore holding BackupLocations indexed by hashes (in bytes)
        public BlobStore Blobs { get; set; }
        public BackupStore BUStore { get; set; }

        public Core(string src, string dst)
        {
            BackuppathSrc = src;
            BackuppathDst = dst;

            BackupIndexDir = Path.Combine(BackuppathDst, "backup");

            // Make sure we have an index folder to write to later
            if (!Directory.Exists(Path.Combine(BackuppathDst, "backup")))
            {
                Directory.CreateDirectory(BackupIndexDir);
            }

            HashIndexFile = Path.Combine(BackuppathDst, "backup", "hashindex");
            BackupListFile = Path.Combine(BackuppathDst, "backup", "backuplist");

            try
            {
                using (FileStream fs = new FileStream(HashIndexFile, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        Blobs = BlobStore.deserialize(reader.ReadBytes((int)fs.Length), HashIndexFile, BackuppathDst); // TODO: make deserialize static like in other classes and move call to deserialize out to Core
                    }
                }
                
            }
            catch
            {
                Blobs = new BlobStore(HashIndexFile, BackuppathDst);
            }

            try
            {
                using (FileStream fs = new FileStream(BackupListFile, FileMode.Open, FileAccess.Read))
                {
                    using (BinaryReader reader = new BinaryReader(fs))
                    {
                        BUStore = BackupStore.deserialize(reader.ReadBytes((int)fs.Length), BackupListFile, Blobs);
                    }
                }
            }
            catch (Exception)
            {
                BUStore = new BackupStore(BackupListFile, Blobs);
            }
        }
        
        public void RunBackupAsync(string message, bool differentialbackup=true, List<Tuple<int, string>> trackpaters=null)
        {
            MetadataTree newmetatree = new MetadataTree(new FileMetadata(BackuppathSrc));
            
            BlockingCollection<string> scanfilequeue = new BlockingCollection<string>();
            BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue = new BlockingCollection<Tuple<string, FileMetadata>>();
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();

            if (differentialbackup)
            {
                BackupRecord previousbackup = BUStore.GetBackupRecord();
                if (previousbackup != null)
                {
                    MetadataTree previousmtree = MetadataTree.deserialize(Blobs.GetBlob(previousbackup.MetadataTreeHash));
                    Task getfilestask = Task.Run(() => GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, previousmtree, trackpaters));
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                Task getfilestask = Task.Run(() => GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, null, trackpaters));
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
                    backupops.Add(Task.Run(() => BackupFileAsync(file, newmetatree)));
                }
            }
            while (!noscanfilequeue.IsCompleted)
            {
                if (noscanfilequeue.TryTake(out Tuple<string, FileMetadata> dir_fmeta))
                {
                    newmetatree.AddFile(dir_fmeta.Item1, dir_fmeta.Item2);
                }
            }
            Task.WaitAll(backupops.ToArray());

            // Add new metadatatree to metastore
            byte[] newmtreebytes = newmetatree.serialize();
            byte[] newmtreehash = Blobs.StoreDataAsync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);

            BUStore.AddBackup(message, newmtreehash);

            // Save "index"
            // Writeout all "dirty" cached index nodes
            Blobs.SynchronizeCacheToDisk(); // TODO: Pass this its path like with MetadataStore
            // Save metadata
            BUStore.SynchronizeCacheToDisk(BackupListFile);
        }

        public void RunBackupSync(string message, bool differentialbackup=true, List<Tuple<int, string>> trackpaterns=null)
        {
            MetadataTree newmetatree = new MetadataTree(new FileMetadata(BackuppathSrc));
            
            BlockingCollection<string> scanfilequeue = new BlockingCollection<string>();
            BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue = new BlockingCollection<Tuple<string, FileMetadata>>();
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();

            if (differentialbackup)
            {
                BackupRecord previousbackup = BUStore.GetBackupRecord();
                if (previousbackup != null)
                {
                    MetadataTree previousmtree = MetadataTree.deserialize(Blobs.GetBlob(previousbackup.MetadataTreeHash));
                    GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, previousmtree, trackpaterns);
                }
                else
                {
                    differentialbackup = false;
                }
            }
            if (!differentialbackup)
            {
                GetFilesAndDirectories(scanfilequeue, noscanfilequeue, directoryqueue, null, null, trackpaterns);
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
                }
            }

            // Add new metadatatree to metastore
            byte[] newmtreebytes = newmetatree.serialize();
            byte[] newmtreehash = Blobs.StoreDataSync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);

            BUStore.AddBackup(message, newmtreehash);

            // Save "index"
            // Writeout entire cached index
            Blobs.SynchronizeCacheToDisk();
            // Save metadata
            BUStore.SynchronizeCacheToDisk(BackupListFile);
        }

        // TODO: Alternate data streams associated with file -> save as ordinary data (will need changes to FileIndex)
        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public void WriteOutFile(string relfilepath, string restorepath, string backuphashprefix=null)
        {
            MetadataTree mtree = MetadataTree.deserialize(Blobs.GetBlob(BUStore.GetBackupRecord(backuphashprefix).MetadataTreeHash));
            FileMetadata filemeta = mtree.GetFile(relfilepath);
            byte[] filedata = Blobs.GetBlob(filemeta.FileHash);
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

        protected void GetFilesAndDirectories(BlockingCollection<string> scanfilequeue, BlockingCollection<Tuple<string, FileMetadata>> noscanfilequeue, 
            BlockingCollection<string> directoryqueue, string path=null, MetadataTree previousmtree=null, List<Tuple<int, string>> trackpaterns=null)
        {
            if (path == null)
            {
                path = BackuppathSrc;
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
                
                foreach (var sd in subDirs)
                {
                    bool trackdir = true;
                    if (trackpaterns != null)
                    {
                        trackdir = CheckTrackAnyDirectoryChild(sd.Substring(BackuppathSrc.Length + 1), trackpaterns);
                    }
                    if (trackdir)
                    {
                        dirs.Push(sd);
                        string relpath = sd.Substring(BackuppathSrc.Length + 1);
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
                    continue;
                }
                catch (DirectoryNotFoundException e)
                {
                    Console.WriteLine(e.Message);
                    continue;
                }

                foreach (var file in files)
                {
                    int trackclass = 3;
                    if (trackpaterns != null)
                    {
                        trackclass = FileTrackClass(file.Substring(BackuppathSrc.Length + 1), trackpaterns);
                    }
                    // Convert file path to a relative path
                    string relpath = file.Substring(BackuppathSrc.Length + 1);
                    switch (trackclass)
                    {
                        case 0: // ignore file completely
                            break;
                        case 1: // Dont scan if we have a previous version
                            bool dontscan = false;
                            if (previousmtree != null)
                            {
                                FileMetadata previousfm = previousmtree.GetFile(relpath);
                                FileMetadata curfm = new FileMetadata(relpath);
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
                                FileMetadata curfm = new FileMetadata(relpath);
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
            }
            directoryqueue.CompleteAdding();
            scanfilequeue.CompleteAdding();
            noscanfilequeue.CompleteAdding();
        }

        public static bool PathMatchesPattern(string path, string pattern)
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
                        return PathMatchesPattern(path.Substring(prefix.Length), wsuffix);
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
                        if (PathMatchesPattern(path, pattern))
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
            int trackclass = 0;
            foreach (var pattern in trackpatterns)
            {
                if (PathMatchesPattern(file, pattern.Item2))
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
            bool track = false;
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
                            else if (PathMatchesPattern(directory, pattern.Item2))
                            {
                                track = false;
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

        public Tuple<int, int> GetBackupSizes(string backuphashstring)
        {
            return Blobs.GetSizes(HashTools.HexStringToByteArray(backuphashstring));
        }

        private void BackupDirectory(string relpath, MetadataTree mtree)
        {
            mtree.AddDirectory(Path.GetDirectoryName(relpath), new FileMetadata(Path.Combine(BackuppathSrc, relpath)));
        }

        protected void BackupFileAsync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(BackuppathSrc, relpath));
            byte[] filehash = Blobs.StoreDataAsync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        // TODO: This should be a relative filepath
        protected void BackupFileSync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(BackuppathSrc, relpath));
            byte[] filehash = Blobs.StoreDataSync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        protected void BackupFileMetadata(string relpath, byte[] filehash, MetadataTree mtree)
        {
            FileMetadata fm = new FileMetadata(Path.Combine(BackuppathSrc, relpath))
            {
                FileHash = filehash
            };
            lock (BUStore)
            {
                mtree.AddFile(Path.GetDirectoryName(relpath), fm);
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns>A list of tuples representing the backup times and their associated messages.</returns>
        public IEnumerable<Tuple<string, DateTime, string>> GetBackups()
        {// TODO: does this need to exist here
            List<Tuple<string, DateTime, string>> backups = new List<Tuple<string, DateTime, string>>();
            foreach (var bh in BUStore.BackupHashes)
            {
                var br = BUStore.GetBackupRecord(bh);
                backups.Add(new Tuple<string, DateTime, string>(HashTools.ByteArrayToHexViaLookup32(bh).ToLower(),
                    br.BackupTime, br.BackupMessage));
            }
            return backups;
        }

        public void RemoveBackup(string backuphashprefix)
        {
            BUStore.RemoveBackup(backuphashprefix);
        }
    }
}
