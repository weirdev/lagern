using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;
using System.Xml;
using System.Runtime.Serialization;

namespace BackupCore
{
    public class Core
    {
        public string backuppath_src { get; set; }

        public string backuppath_dst { get; set; }

        protected SHA1 _hasher = SHA1.Create();
        protected SHA1 hasher { get { return _hasher; } }

        // Key = filepath, Value = list of hashes of blocks
        protected IDictionary<string, IList<byte[]>> FileBlocks = new Dictionary<string, IList<byte[]>>();
        // Key = filepath, Value = File's metadata
        protected IDictionary<string, BasicMetadata> BasicMetaIndex = new Dictionary<string, BasicMetadata>();

        // HashIndexStore holding BackupLocations indexed by hashes (in bytes)
        HashIndexStore hashstore = new HashIndexStore();

        public Core(string src, string dst)
        {
            backuppath_src = src;
            backuppath_dst = dst;
            
        }
        
        public void RunBackup()
        {
            IEnumerable<string> files = GetFiles();
            foreach (var file in files)
            {
                BackupFile(file);
            }

            // Save "index"
            if (!Directory.Exists(Path.Combine(backuppath_dst, "index")))
            {
                Directory.CreateDirectory(Path.Combine(backuppath_dst, "index"));
            }

            //BasicMetadataArray basicmetaarray = new BasicMetadataArray();
            //basicmetaarray.basicmetadataarray = BasicMetaIndex.Values.ToArray();
            
            XmlWriterSettings settings = new XmlWriterSettings();
            settings.WriteEndDocumentOnClose = true;
            settings.Indent = true;
            settings.NewLineOnAttributes = true;
            settings.IndentChars = "    ";

            DataContractSerializer metaserializer = new DataContractSerializer(typeof(IDictionary<string, BasicMetadata>));
            using (XmlWriter writer = XmlWriter.Create(Path.Combine(backuppath_dst, "index", "metadata"), settings))
            {
                metaserializer.WriteObject(writer, BasicMetaIndex);
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
                BackupLocation blocation = hashstore.GetBackupLocation(hash);
                reader = File.OpenRead(Path.Combine(backuppath_dst, blocation.RelativeFilePath));
                buffer = new byte[reader.Length];
                reader.Read(buffer, 0, blocation.ByteLength);
                writer.Write(buffer, 0, blocation.ByteLength);
                reader.Close();
            }
            writer.Close();
            BasicMetaIndex[filepath].WriteOut(Path.Combine(backuppath_dst, relfilepath));
        }

        protected void ImportIndex()
        {
            // Deserialize metadata
            using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "metadata"), FileMode.Open))
            {
                using (XmlDictionaryReader reader =
                    XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, BasicMetadata>));

                    // Deserialize the data and read it from the instance.
                    Dictionary<string, BasicMetadata> importedmetadict =
                        (Dictionary<string, BasicMetadata>)ser.ReadObject(reader, true);
                }
            }
            // Deserialize location dict
            using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "hashindex"), FileMode.Open))
            {
                using (XmlDictionaryReader reader =
                    XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, BackupLocation>));

                    // Deserialize the data and read it from the instance.
                    Dictionary<string, BackupLocation> importedlocationdict =
                        (Dictionary<string, BackupLocation>)ser.ReadObject(reader, true);
                }
            }
            // Deserialize FileBlocks
            using (FileStream fs = new FileStream(Path.Combine(backuppath_dst, "index", "fileblocks"), FileMode.Open))
            {
                using (XmlDictionaryReader reader =
                    XmlDictionaryReader.CreateTextReader(fs, new XmlDictionaryReaderQuotas()))
                {
                    DataContractSerializer ser = new DataContractSerializer(typeof(Dictionary<string, IList<string>>));

                    // Deserialize the data and read it from the instance.
                    Dictionary<string, IList<string>> importedlocationdict =
                        (Dictionary<string, IList<string>>)ser.ReadObject(reader, true);
                }
            }
        }

        protected IEnumerable<string> GetFiles(string path=null)
        {
            if (path == null)
            {
                path = backuppath_src;
            }

            return Directory.EnumerateFiles(path);
        }

        protected IEnumerable<HashBlockPair> GetFileBlocks(string filepath)
        {
            List<HashBlockPair> hashes_blocks = new List<HashBlockPair>();

            MemoryStream newblock = new MemoryStream();
            FileStream reader = File.OpenRead(filepath);
            int readinblocksize = 256;
            // 20 (hash) + 256 (data) = 276
            byte[] buffer = new byte[readinblocksize + 20];
            byte[] hash = new byte[20];
            try
            {
                for (int i = 0; i < reader.Length; i += readinblocksize)
                {
                    // Read into buffer leaving 20 bytes for previous hash
                    reader.Read(buffer, 20, readinblocksize);
                    hash = hasher.ComputeHash(buffer);
                    hash.CopyTo(buffer, 0);
                    // If we reach the end of the file and file.length % readinblocksize != 0
                    // the last block we read in will be < readinblocksize bytes, so we need to 
                    // not write the whole buffer to newblock
                    if (i + readinblocksize > reader.Length)
                    {
                        newblock.Write(buffer, 20, (int)(reader.Length % readinblocksize));
                    }
                    else
                    {
                        newblock.Write(buffer, 20, readinblocksize);
                    }
                    // Last byte is 0
                    if (hash[hash.Length - 1] == 0)
                    {
                        // Hash the 20 byte hash itself because forcing the last two bytes to 0
                        // may cause balancing issues later
                        hashes_blocks.Add(new HashBlockPair(hasher.ComputeHash(hash), newblock.ToArray()));
                        newblock.Dispose();
                        newblock = new MemoryStream();
                        buffer = new byte[readinblocksize + 20];
                    }
                }
                if (newblock.Length != 0)
                {
                    // Hash the hash again for consistency with above
                    hashes_blocks.Add(new HashBlockPair(hasher.ComputeHash(hash), newblock.ToArray()));
                }
            }
            finally
            {
                reader.Close();
            }
            return hashes_blocks;
        }

        protected void BackupFile(string filepath)
        {
            BackupFileMetadata(filepath);
            BackupFileData(filepath);
        }

        protected void BackupFileData(string filepath)
        {
            IEnumerable<HashBlockPair> fileblocks = GetFileBlocks(filepath);

            foreach (HashBlockPair block in fileblocks)
            {
                SaveBlock(block.Hash, block.Block);
                try
                {
                    FileBlocks[filepath].Add(block.Hash);
                }
                catch (KeyNotFoundException)
                {
                    FileBlocks.Add(filepath, new List<byte[]>());
                    FileBlocks[filepath].Add(block.Hash);
                }
            }
        }

        protected void BackupFileMetadata(string filepath)
        {
            BasicMetadata bm = new BasicMetadata(filepath);
            BasicMetaIndex.Add(filepath, bm);
        }

        protected void SaveBlock(byte[] hash, byte[] block)
        {
            string relpath = HashTools.ByteArrayToHexViaLookup32(hash);
            string path = Path.Combine(backuppath_dst, relpath);
            BackupLocation posblocation = new BackupLocation(relpath, 0, block.Length);
            // Have we already stored this 
            if (!hashstore.AddHash(hash, posblocation))
            {
                FileStream writer = File.OpenWrite(path);
                writer.Write(block, 0, block.Length);
                writer.Close();
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
