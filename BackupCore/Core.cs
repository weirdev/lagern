using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Collections.Concurrent;
using System.Threading;

namespace BackupCore
{
    public class Core
    {
        public string backuppath_src { get; set; }

        public string backuppath_dst { get; set; }

        // Key = filepath, Value = list of hashes of blocks
        protected IDictionary<string, IList<byte[]>> FileBlocks = new Dictionary<string, IList<byte[]>>();
        // Key = filepath, Value = File's metadata
        protected IDictionary<string, FileMetadata> FileMetaIndex = new Dictionary<string, FileMetadata>();

        // HashIndexStore holding BackupLocations indexed by hashes (in bytes)
        internal HashIndexStore HashStore { get; set; }

        public Core(string src, string dst)
        {
            backuppath_src = src;
            backuppath_dst = dst;

            // Make sure we have an index folder to write to later
            if (!Directory.Exists(Path.Combine(backuppath_dst, "index")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            HashStore = new HashIndexStore(Path.Combine(backuppath_dst, "index", "hashindex"));
            
        }
        
        public void RunBackupAsync()
        {
            // Make sure we have an index folder to write to later
            if (!Directory.Exists(Path.Combine(backuppath_dst, "index")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            if (!Directory.Exists(Path.Combine(backuppath_dst, "index", "hashindex")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            BlockingCollection<string> filequeue = new BlockingCollection<string>();
            Task getfilestask = Task.Run(() => GetFiles(filequeue));
            
            List<Task> backupops = new List<Task>();
            while (!filequeue.IsCompleted)
            {
                string file;
                if (filequeue.TryTake(out file))
                {
                    backupops.Add(Task.Run(() => BackupFileAsync(file)));
                }
            }
            Task.WaitAll(backupops.ToArray());


            // Save "index"
            // Writeout all "dirty" cached index nodes
            HashStore.SynchronizeCacheToDisk();
            
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.WriteEndDocumentOnClose = true;
            settings.Indent = true;
            settings.NewLineOnAttributes = true;
            settings.IndentChars = "    ";

            DataContractSerializer metaserializer = new DataContractSerializer(typeof(IDictionary<string, FileMetadata>));
            using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "metadata"), settings))
            {
                metaserializer.WriteObject(writer, FileMetaIndex);
            }

            // TODO : Ability to read in HashIndexStore
            //DataContractSerializer locdictserializer = new DataContractSerializer(typeof(Dictionary<string, BackupLocation>));
            //using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "hashindex"), settings))
            //{
            //    locdictserializer.WriteObject(writer, LocationDict);
            //}

            // TODO : Ability to serialize Dict with byte[]
            //DataContractSerializer fileblockserializer = new DataContractSerializer(typeof(Dictionary<string, IList<string>>));
            //using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "fileblocks"), settings))
            //{
            //    fileblockserializer.WriteObject(writer, FileBlocks);
            //}
        }

        public void RunBackupSync()
        {
            BlockingCollection<string> filequeue = new BlockingCollection<string>();
            GetFiles(filequeue);
            
            while (!filequeue.IsCompleted)
            {
                string file;
                if (filequeue.TryTake(out file))
                {
                    BackupFileSync(file);
                }
            }


            // Save "index"
            // Writeout entire cached index
            HashStore.SynchronizeCacheToDisk();

            // Metadata
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.WriteEndDocumentOnClose = true;
            settings.Indent = true;
            settings.NewLineOnAttributes = true;
            settings.IndentChars = "    ";

            DataContractSerializer metaserializer = new DataContractSerializer(typeof(IDictionary<string, FileMetadata>));
            using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "metadata"), settings))
            {
                metaserializer.WriteObject(writer, FileMetaIndex);
            }

            // TODO : Ability to read in HashIndexStore
            //DataContractSerializer locdictserializer = new DataContractSerializer(typeof(Dictionary<string, BackupLocation>));
            //using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "hashindex"), settings))
            //{
            //    locdictserializer.WriteObject(writer, LocationDict);
            //}

            // TODO : Ability to serialize Dict with byte[]
            //DataContractSerializer fileblockserializer = new DataContractSerializer(typeof(Dictionary<string, IList<string>>));
            //using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "fileblocks"), settings))
            //{
            //    fileblockserializer.WriteObject(writer, FileBlocks);
            //}
        }

        // TODO: Alternate data streams associated with file -> save as ordinary data (will need changes to FileIndex)
        // TODO: ReconstructFile() doesnt produce exactly original file
        public void ReconstructFile(string relfilepath)
        {
            //ImportIndex();
            string filepath = Path.Combine(backuppath_src, relfilepath);

            FileStream reader;
            byte[] buffer;
            FileStream writer = File.OpenWrite(Path.Combine(backuppath_dst, relfilepath));
            foreach (var hash in FileBlocks[filepath])
            {
                BackupLocation blocation = HashStore.GetBackupLocation(hash);
                reader = File.OpenRead(Path.Combine(backuppath_dst, blocation.RelativeFilePath));
                buffer = new byte[reader.Length];
                reader.Read(buffer, 0, blocation.ByteLength);
                writer.Write(buffer, 0, blocation.ByteLength);
                reader.Close();
            }
            writer.Close();
            FileMetaIndex[filepath].WriteOut(Path.Combine(backuppath_dst, relfilepath));
        }

        protected void ImportIndex()
        {
            // Deserialize metadata
            using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "metadata"), FileMode.Open))
            {
                using (XmlDictionaryReader reader =
                    XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, FileMetadata>));

                    // Deserialize the data and read it from the instance.
                    Dictionary<string, FileMetadata> importedmetadict =
                        (Dictionary<string, FileMetadata>)ser.ReadObject(reader, true);
                }
            }
            //// Deserialize location dict
            //using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "hashindex"), FileMode.Open))
            //{
            //    using (XmlDictionaryReader reader =
            //        XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
            //    {
            //        DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, BackupLocation>));

            //        // Deserialize the data and read it from the instance.
            //        Dictionary<string, BackupLocation> importedlocationdict =
            //            (Dictionary<string, BackupLocation>)ser.ReadObject(reader, true);
            //    }
            //}
            //// Deserialize FileBlocks
            //using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "fileblocks"), FileMode.Open))
            //{
            //    using (XmlDictionaryReader reader =
            //        XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
            //    {
            //        DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, IList<string>>));

            //        // Deserialize the data and read it from the instance.
            //        Dictionary<string, IList<string>> importedlocationdict =
            //            (Dictionary<string, IList<string>>)ser.ReadObject(reader, true);
            //    }
            //}
        }

        protected void GetFiles(BlockingCollection<string> filequeue, string path=null)
        {
            if (path == null)
            {
                path = backuppath_src;
            }

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
                    filequeue.Add(file);
                }
            }
            filequeue.CompleteAdding();
        }

        protected void GetFileBlocks(BlockingCollection<HashBlockPair> hashblockqueue, string filepath)
        {
            MemoryStream newblock = new MemoryStream();
            FileStream readerbuffer = File.OpenRead(filepath);
            byte[] buffer = new byte[1];
            byte[] hash = new byte[16];
            byte[][] hashes = new byte[4096][];
            int lastblock = 0;
            try
            {
                for (int i = 0; i < readerbuffer.Length; i += 8388608)
                {
                    byte[] readin;
                    if (i + 8388608 <= readerbuffer.Length)
                    {
                        readin = new byte[8388608];
                        readerbuffer.Read(readin, 0, 8388608);
                    }
                    else
                    {
                        readin = new byte[readerbuffer.Length % 8388608];
                        readerbuffer.Read(readin, 0, (int)(readerbuffer.Length % 8388608));
                    }
                    for (int j = 0; j < readin.Length; j++)
                    {
                        byte[] hashaddition = HashTools.md5hashes[readin[j]];
                        for (int k = 0; k < hashaddition.Length; k++)
                        {
                            hash[k] = (byte)(hash[k] ^ hashaddition[k]);
                        }
                        if (hashes[(j + 1) % 4096] != null)
                        {
                            for (int k = 0; k < hashes[(j + 1) % 4096].Length; k++)
                            {
                                hash[k] = (byte)(hash[k] ^ hashes[(j + 1) % 4096][k]);
                            }
                        }
                        hashes[j % 4096] = (byte[])hash.Clone();
                        newblock.Write(buffer, 0, 1);
                        // Last byte is 0
                        // Third to last use only upper nibble
                        int third = hash[hash.Length - 3] - 1;
                        if (third < 0)
                        {
                            third = 0;
                        }
                        if (hash[hash.Length - 1] + hash[hash.Length - 2] + third == 0)
                        {
                            Console.WriteLine(i + j - lastblock);
                            lastblock = i + j;
                            hashblockqueue.Add(new HashBlockPair(HashTools.sha1hasher.ComputeHash(newblock.ToArray()), newblock.ToArray()));
                            newblock.Dispose();
                            newblock = new MemoryStream();
                            buffer = new byte[1];
                            hash = new byte[16];
                            hashes = new byte[4096][];
                        }
                    }
                }
                if (newblock.Length != 0)
                {
                    Console.WriteLine(newblock.Length);
                    // Hash the hash again for consistency with above
                    hashblockqueue.Add(new HashBlockPair(HashTools.sha1hasher.ComputeHash(newblock.ToArray()), newblock.ToArray()));
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

        protected void BackupFileAsync(string filepath)
        {
            BackupFileMetadata(filepath);
            BackupFileDataAsync(filepath);
        }

        // TODO: This should be a relative filepath
        protected void BackupFileSync(string filepath)
        {
            BackupFileMetadata(filepath);
            BackupFileDataSync(filepath);
        }

        protected void BackupFileDataAsync(string filepath)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            Task getfileblockstask = Task.Run(() => GetFileBlocks(fileblockqueue, filepath));

            
            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    SaveBlock(block.Hash, block.Block);
                    try
                    {
                        lock (FileBlocks[filepath])
                        {
                            FileBlocks[filepath].Add(block.Hash);
                        }
                    }
                    catch (Exception)
                    {
                        lock (FileBlocks)
                        {
                            FileBlocks.Add(filepath, new List<byte[]>());
                            FileBlocks[filepath].Add(block.Hash);
                        }
                    }
                }
            }
        }

        protected void BackupFileDataSync(string filepath)
        {
            BlockingCollection<HashBlockPair> fileblockqueue = new BlockingCollection<HashBlockPair>();
            GetFileBlocks(fileblockqueue, filepath);


            while (!fileblockqueue.IsCompleted)
            {
                HashBlockPair block;
                if (fileblockqueue.TryTake(out block))
                {
                    SaveBlock(block.Hash, block.Block);
                    try
                    {
                        lock (FileBlocks[filepath])
                        {
                            FileBlocks[filepath].Add(block.Hash);
                        }
                    }
                    catch (Exception)
                    {
                        lock (FileBlocks)
                        {
                            FileBlocks.Add(filepath, new List<byte[]>());
                            FileBlocks[filepath].Add(block.Hash);
                        }
                    }
                }
            }
        }

        protected void BackupFileMetadata(string filepath)
        {
            FileMetadata bm = new FileMetadata(filepath);
            FileMetaIndex.Add(filepath, bm);
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

        public string GetRelativePath(string fullpath, string basepath)
        {
            if (!fullpath.StartsWith(basepath))
            {
                throw new ArgumentException("basepath doesn't prefix fullpath");
            }
            return fullpath.Substring(basepath.Length);
        }
    }
}
