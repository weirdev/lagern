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
        public string backuppath_src { get; set; }

        public string backuppath_dst { get; set; }

        // BlockHashStore holding BackupLocations indexed by hashes (in bytes)
        private BlobStore Blobs { get; set; }
        private BackupStore BUStore { get; set; }

        public Core(string src, string dst)
        {
            backuppath_src = src;
            backuppath_dst = dst;

            // Make sure we have an index folder to write to later
            if (!Directory.Exists(Path.Combine(backuppath_dst, "index")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            Blobs = new BlobStore(Path.Combine(backuppath_dst, "index", "hashindex"), backuppath_dst);
            BUStore = new BackupStore(Path.Combine(backuppath_dst, "index", "metadata"), this);
        }
        
        public void RunBackupAsync(string message)
        {
            MetadataTree newmetatree = new MetadataTree();

            newmetatree.AddDirectory("\\", new FileMetadata(backuppath_src));
            BlockingCollection<string> filequeue = new BlockingCollection<string>();
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();
            Task getfilestask = Task.Run(() => GetFilesAndDirectories(filequeue, directoryqueue));
            
            List<Task> backupops = new List<Task>();
            while (!directoryqueue.IsCompleted)
            {
                string directory;
                if (directoryqueue.TryTake(out directory))
                {
                    // We do not backup diretories asychronously
                    // becuase a. they should not take long anyway
                    // and b. the metadatastore needs to have stored directories
                    // before it stores their children.
                    BackupDirectory(directory, newmetatree);
                }
            }
            while (!filequeue.IsCompleted)
            {
                string file;
                if (filequeue.TryTake(out file))
                {
                    backupops.Add(Task.Run(() => BackupFileAsync(file, newmetatree)));
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
            BUStore.SynchronizeCacheToDisk(Path.Combine(backuppath_dst, "index", "metadata"));
        }

        public void RunBackupSync(string message)
        {
            MetadataTree newmetatree = new MetadataTree();

            newmetatree.AddDirectory("\\", new FileMetadata(backuppath_src));
            BlockingCollection<string> filequeue = new BlockingCollection<string>();
            BlockingCollection<string> directoryqueue = new BlockingCollection<string>();
            GetFilesAndDirectories(filequeue, directoryqueue);

            while (!directoryqueue.IsCompleted)
            {
                string directory;
                if (directoryqueue.TryTake(out directory))
                {
                    // We backup directories first because
                    // the metadatastore needs to have stored directories
                    // before it stores their children.
                    BackupDirectory(directory, newmetatree);
                }
            }
            while (!filequeue.IsCompleted)
            {
                string file;
                if (filequeue.TryTake(out file))
                {
                    BackupFileSync(file, newmetatree);
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
            BUStore.SynchronizeCacheToDisk(Path.Combine(backuppath_dst, "index", "metadata"));
        }

        // TODO: Alternate data streams associated with file -> save as ordinary data (will need changes to FileIndex)
        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public void WriteOutFile(string relfilepath, string restorepath, string backuphash=null)
        {
            MetadataTree mtree = MetadataTree.deserialize(Blobs.GetBlob(BUStore[backuphash].MetadataTreeHash));
            FileMetadata filemeta = mtree.GetFile(relfilepath);
            byte[] filedata = Blobs.GetBlob(filemeta.FileHash);
            // TODO: autoreplacing of '/' with '\\'
            using (FileStream writer = new FileStream(restorepath, FileMode.OpenOrCreate)) // the more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
            {
                writer.Write(filedata, 0, filedata.Length);
                // Flush the writer in order to get a correct stream position for truncating
                writer.Flush();
                // Set the stream length to the current position in order to truncate leftover data in original file
                writer.SetLength(writer.Position);

            }
            filemeta.WriteOutMetadata(restorepath);
        }

        public MetadataTree GetMetadataTree(string backuphash)
        {
            return MetadataTree.deserialize(Blobs.GetBlob(BUStore[backuphash].MetadataTreeHash));
        }

        protected void GetFilesAndDirectories(BlockingCollection<string> filequeue, BlockingCollection<string> directoryqueue, string path=null)
        {
            if (path == null)
            {
                path = backuppath_src;
            }

            // TODO: Bigger stack?
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
                    dirs.Push(sd);
                    string relpath = sd.Substring(backuppath_src.Length + 1);
                    directoryqueue.Add(relpath);
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
                    // Convert file path to a relative path
                    string relpath = file.Substring(backuppath_src.Length + 1);
                    filequeue.Add(relpath);
                }
            }
            directoryqueue.CompleteAdding();
            filequeue.CompleteAdding();
        }

        private void BackupDirectory(string relpath, MetadataTree mtree)
        {
            mtree.AddDirectory(Path.GetDirectoryName(relpath), new FileMetadata(Path.Combine(backuppath_src, relpath)));
        }

        protected void BackupFileAsync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            byte[] filehash = Blobs.StoreDataAsync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        // TODO: This should be a relative filepath
        protected void BackupFileSync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            byte[] filehash = Blobs.StoreDataSync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        protected void BackupFileMetadata(string relpath, byte[] filehash, MetadataTree mtree)
        {
            FileMetadata fm = new FileMetadata(Path.Combine(backuppath_src, relpath));
            fm.FileHash = filehash;
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
            return from backup in BUStore select new Tuple<string, DateTime, string>(HashTools.ByteArrayToHexViaLookup32(backup.MetadataTreeHash).ToLower(), backup.BackupTime, backup.BackupMessage);
        }

        public void RemoveBackup(string backuphash)
        {
            throw new NotImplementedException();
        }
    }
}
