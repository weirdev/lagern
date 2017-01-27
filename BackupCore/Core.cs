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
    public class Core
    {
        public string backuppath_src { get; set; }

        public string backuppath_dst { get; set; }

        // HashIndexStore holding BackupLocations indexed by hashes (in bytes)
        private HashIndexStore HashStore { get; set; }
        private MetadataStore MetaStore { get; set; }

        public Core(string src, string dst)
        {
            backuppath_src = src;
            backuppath_dst = dst;

            // Make sure we have an index folder to write to later
            if (!Directory.Exists(Path.Combine(backuppath_dst, "index")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            HashStore = new HashIndexStore(Path.Combine(backuppath_dst, "index", "hashindex"), backuppath_dst);
            MetaStore = new MetadataStore(Path.Combine(backuppath_dst, "index", "metadata"));
        }
        
        public void RunBackupAsync(string message)
        {
            MetadataTree newmetatree = new MetadataTree();

            newmetatree.AddDirectory(".", new FileMetadata(backuppath_src));
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
            MemoryStream mtreestream = new MemoryStream(newmtreebytes);
            List<byte[]> newmtreehashes = BackupFileDataAsync(mtreestream);
            MetaStore.AddBackup(message, newmtreehashes);

            // Save "index"
            // Writeout all "dirty" cached index nodes
            HashStore.SynchronizeCacheToDisk(); // TODO: Pass this its path like with MetadataStore
            // Save metadata
            MetaStore.SynchronizeCacheToDisk(Path.Combine(backuppath_dst, "index", "metadata"));
        }

        public void RunBackupSync(string message)
        {
            MetadataTree newmetatree = new MetadataTree();

            newmetatree.AddDirectory(".", new FileMetadata(backuppath_src));
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
            MemoryStream mtreestream = new MemoryStream(newmtreebytes);
            List<byte[]> newmtreehashes = BackupFileDataSync(mtreestream);
            MetaStore.AddBackup(message, newmtreehashes);

            // Save "index"
            // Writeout entire cached index
            HashStore.SynchronizeCacheToDisk();
            // Save metadata
            MetaStore.SynchronizeCacheToDisk(Path.Combine(backuppath_dst, "index", "metadata"));
        }

        // TODO: Alternate data streams associated with file -> save as ordinary data (will need changes to FileIndex)
        /// <summary>
        /// Restore a backed up file. Includes metadata.
        /// </summary>
        /// <param name="relfilepath"></param>
        /// <param name="restorepath"></param>
        /// <param name="backupindex"></param>
        public void WriteOutFile(string relfilepath, string restorepath, int backupindex=-1)
        {
            // We assume the latest backup
            if (backupindex == -1)
            {
                backupindex = MetaStore.Count - 1;
            }
            MetadataTree latestmtree = MetadataTree.deserialize(HashStore.ReconstructFileData(MetaStore[backupindex].MetadataTreeHashes));
            FileMetadata filemeta = latestmtree.GetFile(relfilepath);
            byte[] filedata = HashStore.ReconstructFileData(filemeta.BlocksHashes);
            using (FileStream writer = new FileStream(restorepath, FileMode.Create))
            {
                writer.Write(filedata, 0, filedata.Length);
            }
            filemeta.WriteOutMetadata(restorepath);
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
            mtree.AddDirectory(relpath, new FileMetadata(Path.Combine(backuppath_src, relpath)));
        }

        protected void GetFileBlocks(BlockingCollection<HashBlockPair> hashblockqueue, Stream readerbuffer)
        {
            MemoryStream newblock = new MemoryStream();
            SHA1 sha1hasher = SHA1.Create();

            int readsize = 8388608;
            byte[] hash = new byte[16];
            int lastblock = 0;
            try
            {
                for (int i = 0; i < readerbuffer.Length; i += readsize) // read the file in larger chunks for efficiency
                {
                    byte[] readin;
                    if (i + readsize <= readerbuffer.Length) // readsize or more bytes left to read
                    {
                        readin = new byte[readsize];
                        readerbuffer.Read(readin, 0, readsize);
                    }
                    else // < readsize bytes left to read
                    {
                        readin = new byte[readerbuffer.Length % readsize];
                        readerbuffer.Read(readin, 0, (int)(readerbuffer.Length % readsize));
                    }
                    for (int j = 0; j < readin.Length; j++) // Byte by byte
                    {
                        newblock.Write(readin, j, 1);
                        // TODO: This is sloppy and a real checksum would probably be better.
                        // Get hash of single byte
                        byte[] hashaddition = HashTools.md5hashes[readin[j]];
                        // XOR hash with result of previous hashes
                        for (int k = 0; k < hashaddition.Length; k++)
                        {
                            hash[k] = (byte)(hash[k] ^ hashaddition[k]);
                        }

                        if (hash[hash.Length - 1] == 0 && hash[hash.Length - 2] == 0)
                        {
                            // Last byte is 0
                            // Third to last use only upper nibble
                            int third = hash[hash.Length - 3] & 15;
                            if (third == 0)
                            {
                                lastblock = i + j;
                                byte[] block = newblock.ToArray();
                                hashblockqueue.Add(new HashBlockPair(sha1hasher.ComputeHash(block), block));
                                newblock.Dispose();
                                newblock = new MemoryStream();
                                hash = new byte[16];
                            }
                        }
                    }
                }
                if (newblock.Length != 0) // Create block from remaining bytes
                {
                    byte[] block = newblock.ToArray();
                    hashblockqueue.Add(new HashBlockPair(sha1hasher.ComputeHash(block), block));
                    newblock.Dispose();
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Error");
            }
            finally
            {
                readerbuffer.Close();
            }
            hashblockqueue.CompleteAdding();
        }

        protected void BackupFileAsync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            List<byte[]> blockshashes = BackupFileDataAsync(readerbuffer);
            BackupFileMetadata(relpath, blockshashes, mtree);
        }

        // TODO: This should be a relative filepath
        protected void BackupFileSync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            List<byte[]> blockshashes = BackupFileDataSync(readerbuffer);
            BackupFileMetadata(relpath, blockshashes, mtree);
        }

        protected List<byte[]> BackupFileDataAsync(Stream readerbuffer)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            
            Task getfileblockstask = Task.Run(() => GetFileBlocks(fileblockqueue, readerbuffer));

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    SaveBlock(block.Hash, block.Block);
                    blockshashes.Add(block.Hash);
                }
            }
            return blockshashes;
        }

        /// <summary>
        /// Backup a file's data asychronously as its blocks become available.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        protected List<byte[]> BackupFileDataSync(Stream readerbuffer)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            GetFileBlocks(fileblockqueue, readerbuffer);

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    SaveBlock(block.Hash, block.Block);
                    blockshashes.Add(block.Hash);
                }
            }
            return blockshashes;
        }

        protected void BackupFileMetadata(string relpath, List<byte[]> blockshashes, MetadataTree mtree)
        {
            FileMetadata fm = new FileMetadata(Path.Combine(backuppath_src, relpath));
            fm.BlocksHashes = blockshashes;
            // TODO: will need to add subdirectories before their files
            lock (MetaStore)
            {
                mtree.AddFile(relpath, fm);
            }
        }

        protected void SaveBlock(byte[] hash, byte[] block)
        {
            string relpath = HashTools.ByteArrayToHexViaLookup32(hash);
            string path = Path.Combine(backuppath_dst, relpath);
            BackupLocation posblocation = new BackupLocation(relpath, 0, block.Length);
            bool alreadystored = false;
            lock (HashStore)
            {
                // Have we already stored this 
                alreadystored = HashStore.AddHash(hash, posblocation);
            }
            if (!alreadystored)
            {
                using (FileStream writer = File.OpenWrite(path))
                {
                    writer.Write(block, 0, block.Length);
                    writer.Flush();
                    writer.Close();
                }
            }
        }
        
        /// <summary>
        /// 
        /// </summary>
        /// <returns>A list of tuples representing the backup times and their associated messages.</returns>
        public IEnumerable<Tuple<DateTime, string>> GetBackups()
        {
            return from backup in MetaStore select new Tuple<DateTime, string>(backup.BackupTime, backup.BackupMessage);
        }
    }
}
