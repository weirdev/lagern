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
            byte[] newmtreehash = StoreDataAsync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);

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
            byte[] newmtreehash = StoreDataSync(newmtreebytes, BlobLocation.BlobTypes.MetadataTree);

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
            MetadataTree mtree = MetadataTree.deserialize(Blobs.ReconstructFileData(BUStore[backuphash].MetadataTreeHash));
            FileMetadata filemeta = mtree.GetFile(relfilepath);
            byte[] filedata = Blobs.ReconstructFileData(filemeta.FileHash);
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
            return MetadataTree.deserialize(Blobs.ReconstructFileData(BUStore[backuphash].MetadataTreeHash));
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

        /// <summary>
        /// Wraps other storedata method using input stream. Creates MemoryStream from inputdata.
        /// </summary>
        /// <param name="inputdata"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblockqueue"></param>
        protected void SplitData(byte[] inputdata, BlobLocation.BlobTypes type, byte[] filehash, BlockingCollection<HashBlockPair> hashblockqueue)
        {
            SplitData(new MemoryStream(inputdata), type, filehash, hashblockqueue);
        }

        /// <summary>
        /// Chunks and saves data to blobstore. Operates on stream input, so Filestreams can be used and entire files need not be loaded into memory.
        /// </summary>
        /// <param name="inputstream"></param>
        /// <param name="type"></param>
        /// <param name="filehash"></param>
        /// <param name="hashblockqueue"></param>
        protected void SplitData(Stream inputstream, BlobLocation.BlobTypes type, byte[] filehash, BlockingCollection<HashBlockPair> hashblockqueue)
        {
            // https://rsync.samba.org/tech_report/node3.html
            List<byte> newblock = new List<byte>();
            byte[] alphachksum = new byte[16];
            byte[] betachksum = new byte[16];
            SHA1 sha1filehasher = SHA1.Create();
            SHA1 sha1blockhasher = SHA1.Create();


            int readsize = 8388608;
            int rollwindowsize = 32;
            try
            {
                for (int i = 0; i < inputstream.Length; i += readsize) // read the file in larger chunks for efficiency
                {
                    byte[] readin;
                    if (i + readsize <= inputstream.Length) // readsize or more bytes left to read
                    {
                        readin = new byte[readsize];
                        inputstream.Read(readin, 0, readsize);
                    }
                    else // < readsize bytes left to read
                    {
                        readin = new byte[inputstream.Length % readsize];
                        inputstream.Read(readin, 0, (int)(inputstream.Length % readsize));
                    }
                    for (int j = 0; j < readin.Length; j++) // Byte by byte
                    {
                        newblock.Add(readin[j]);
                        HashTools.ByteSum(alphachksum, newblock[newblock.Count - 1]);
                        if (newblock.Count > rollwindowsize)
                        {
                            HashTools.ByteDifference(alphachksum, newblock[newblock.Count - rollwindowsize - 1]);
                            byte[] shifted = new byte[16];
                            shifted[0] = (byte)((newblock[newblock.Count - 1] << 5) & 0xFF); // rollwindowsize = 32 = 2^5 => 5
                            shifted[1] = (byte)((newblock[newblock.Count - 1] >> 3) & 0xFF); // 8-5 = 3
                            HashTools.BytesDifference(betachksum, shifted);
                        }
                        HashTools.BytesSum(betachksum, alphachksum);
                        

                        if (alphachksum[15] == 0xFF && betachksum[0] == 0xFF && betachksum[15] == 0xFE) // (256*256*128)^-1 => expected value (/2) = ~4MB
                        {
                            byte[] block = newblock.ToArray();
                            if (i >= inputstream.Length && j >= readin.Length) // Need to use TransformFinalBlock if at end of input
                            {
                                sha1filehasher.TransformFinalBlock(block, 0, block.Length);
                            }
                            else
                            {
                                sha1filehasher.TransformBlock(block, 0, block.Length, block, 0);
                            }
                            hashblockqueue.Add(new HashBlockPair(sha1blockhasher.ComputeHash(block), block));
                            newblock = new List<byte>();
                        }
                    }
                }
                if (newblock.Count != 0) // Create block from remaining bytes
                {
                    byte[] block = newblock.ToArray();
                    sha1filehasher.TransformFinalBlock(block, 0, block.Length);
                    hashblockqueue.Add(new HashBlockPair(sha1blockhasher.ComputeHash(block), block));
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Error");
            }
            finally
            {
                inputstream.Close();
            }
            Array.Copy(sha1filehasher.Hash, filehash, sha1filehasher.Hash.Length);
            hashblockqueue.CompleteAdding();
        }

        protected void BackupFileAsync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            byte[] filehash = StoreDataAsync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        // TODO: This should be a relative filepath
        protected void BackupFileSync(string relpath, MetadataTree mtree)
        {
            FileStream readerbuffer = File.OpenRead(Path.Combine(backuppath_src, relpath));
            byte[] filehash = StoreDataSync(readerbuffer, BlobLocation.BlobTypes.FileBlob);
            BackupFileMetadata(relpath, filehash, mtree);
        }

        protected byte[] StoreDataAsync(byte[] inputdata, BlobLocation.BlobTypes type)
        {
            return StoreDataAsync(new MemoryStream(inputdata), type);
        }

        protected byte[] StoreDataAsync(Stream readerbuffer, BlobLocation.BlobTypes type)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            Task getfileblockstask = Task.Run(() => SplitData(readerbuffer, type, filehash, fileblockqueue));

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    Blobs.AddBlob(block, BlobLocation.BlobTypes.Simple);
                    blockshashes.Add(block.Hash);
                }
            }
            if (blockshashes.Count > 1)
            {
                // Multiple blocks so create hashlist blob to reference them all together
                byte[] hashlist = new byte[blockshashes.Count * blockshashes[0].Length];
                for (int i = 0; i < blockshashes.Count; i++)
                {
                    Array.Copy(blockshashes[i], 0, hashlist, i * blockshashes[i].Length, blockshashes[i].Length);
                }
                Blobs.AddMultiBlockReferenceBlob(filehash, hashlist, BlobLocation.BlobTypes.FileBlob);
            }
            else
            {
                // Just the one block, so change its type to FileBlob
                Blobs.GetBackupLocation(filehash).BlobType = BlobLocation.BlobTypes.FileBlob; // filehash should match individual block hash used earlier since total file == single block
            }
            return filehash;
        }

        protected byte[] StoreDataSync(byte[] inputdata, BlobLocation.BlobTypes type)
        {
            return StoreDataSync(new MemoryStream(inputdata), type);
        }

        /// <summary>
        /// Backup data sychronously.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns>A list of hashes representing the file contents.</returns>
        protected byte[] StoreDataSync(Stream readerbuffer, BlobLocation.BlobTypes type)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            byte[] filehash = new byte[20]; // Overall hash of file
            SplitData(readerbuffer, type, filehash, fileblockqueue);

            List<byte[]> blockshashes = new List<byte[]>();
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    Blobs.AddBlob(block, BlobLocation.BlobTypes.Simple);
                    blockshashes.Add(block.Hash);
                }
            }
            if (blockshashes.Count > 1)
            {
                // Multiple blocks so create hashlist blob to reference them all together
                byte[] hashlist = new byte[blockshashes.Count * blockshashes[0].Length];
                for (int i = 0; i < blockshashes.Count; i++)
                {
                    Array.Copy(blockshashes[i], 0, hashlist, i * blockshashes[i].Length, blockshashes[i].Length);
                }
                Blobs.AddMultiBlockReferenceBlob(filehash, hashlist, BlobLocation.BlobTypes.FileBlob);
            }
            else
            {
                // Just the one block, so change its type to FileBlob
                Blobs.GetBackupLocation(filehash).BlobType = BlobLocation.BlobTypes.FileBlob; // filehash should match individual block hash used earlier since total file == single block
            }
            return filehash;
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
