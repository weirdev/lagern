using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace BackupCore
{
    public class VirtualFSInterop : IFSInterop, IDstFSInterop
    {
        public MetadataNode VirtualFS { get; private set; }

        // Note that this dataStore is just the backing key-value store for the virtual files
        // It does not have any deduplication, although if this virtual fs is used as a dst,
        // already deduplicated files may be stored in it. This data store also does not reference
        // count stored data, so no data is ever deleted from it.
        private IDictionary<byte[], byte[]> DataStore { get; set; }

        public string DstRoot { get; private set; }

        public AesHelper? Encryptor { get; private set; }

        public VirtualFSInterop(MetadataNode filesystem, BPlusTree<byte[]> datastore)
        {
            VirtualFS = filesystem;
            DataStore = datastore;
            DstRoot = "<<Non dest VFS interop>>";
        }

        public static async Task<IDstFSInterop> InitializeNewDst(MetadataNode filesystem, BPlusTree<byte[]> datastore, string dstRoot, string? password=null)
        {
            VirtualFSInterop virtualFSInterop = new(filesystem, datastore);
            virtualFSInterop.DstRoot = dstRoot;
            if (password != null)
            {
                AesHelper encryptor = AesHelper.CreateFromPassword(password);
                byte[] keyfile = encryptor.CreateKeyFile();
                await virtualFSInterop.StoreIndexFileAsync(null, IndexFileType.EncryptorKeyFile, keyfile);
                virtualFSInterop.Encryptor = encryptor;
            }
            return virtualFSInterop;
        }

        public static async Task<IDstFSInterop> LoadDst(MetadataNode filesystem, BPlusTree<byte[]> datastore, string dstRoot, string? password=null)
        {
            VirtualFSInterop virtualFSInterop = new(filesystem, datastore);
            virtualFSInterop.DstRoot = dstRoot;
            if (password != null)
            {
                byte[] keyfile = await virtualFSInterop.LoadIndexFileAsync(null, IndexFileType.EncryptorKeyFile);
                AesHelper encryptor = AesHelper.CreateFromKeyFile(keyfile, password);
                virtualFSInterop.Encryptor = encryptor;
            }
            return virtualFSInterop;
        }

        public Task<bool> FileExists(string absolutepath) => Task.FromResult(VirtualFS.GetFile(absolutepath) != null);

        public Task<bool> DirectoryExists(string absolutepath) => Task.FromResult(VirtualFS.GetDirectory(absolutepath) != null);

        public async Task CreateDirectoryIfNotExists(string absolutepath)
        {
            if (!await DirectoryExists(absolutepath))
            {
                VirtualFS.AddDirectory(Path.GetDirectoryName(absolutepath) ?? throw new NullReferenceException("This path must exist"), 
                    MakeNewDirectoryMetadata(Path.GetFileName(absolutepath)));
            }
        }

        public string BlobSaveDirectory
        {
            get => Path.Combine(DstRoot, "blobdata");
        }

        public Task<byte[]> ReadAllFileBytes(string absolutepath)
        {
            var file = VirtualFS.GetFile(absolutepath);
            if (file == null)
            {
                throw new NullReferenceException("File not found");
            }
            if (file.FileHash == null)
            {
                throw new NullReferenceException("Missing file hash");
            }
            return Task.FromResult(DataStore[file.FileHash]);
        }

        public Task<FileMetadata> GetFileMetadata(string absolutepath)
        {
            var md = VirtualFS.GetFile(absolutepath);
            if (md == null)
            {
                var dir = VirtualFS.GetDirectory(absolutepath);
                if (dir == null)
                {
                    throw new FileNotFoundException();
                }
                md = dir.DirMetadata;
            }
            return Task.FromResult(md);
        }

        public async Task<Stream> GetFileData(string absolutepath) => new MemoryStream(await ReadAllFileBytes(absolutepath));

        public Task<string[]> GetDirectoryFiles(string absolutepath)
        {
            var dir = VirtualFS.GetDirectory(absolutepath) ?? throw new NullReferenceException("Directory not found");
            return Task.FromResult(dir.Files.Keys.Select(file => Path.Combine(absolutepath, file)).ToArray());
        }

        public Task OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata? fileMetadata = null)
        {
            var root = VirtualFS;
            string[] path = absolutepath.Split(Path.DirectorySeparatorChar);
            path = path.Take(path.Length - 1).ToArray();
            foreach (var dir in path)
            {
                if (!root.HasDirectory(dir))
                {
                    root = root.AddDirectory(MakeNewDirectoryMetadata(dir));
                }
                else
                {
                    var newRoot = root.GetDirectory(dir);
                    if (newRoot == null)
                    {
                        throw new Exception("This directory should always exist");
                    }
                    root = newRoot;
                }
            }
            var datahash = StoreDataGetHash(data);
            if (fileMetadata == null)
            {
                fileMetadata = MakeNewFileMetadata(Path.GetFileName(absolutepath), data.Length, datahash);
            }
            string dirpath = Path.GetDirectoryName(absolutepath) ?? throw new Exception("This path should always exist");
            var directory = VirtualFS.GetDirectory(dirpath) ?? throw new Exception("This directory should always exist");
            directory.Files[Path.GetFileName(absolutepath)] = fileMetadata;
            return Task.CompletedTask;
        }

        public Task<string[]> GetSubDirectories(string absolutepath)
        {
            var dir = VirtualFS.GetDirectory(absolutepath) ?? throw new NullReferenceException("Directory not found");
            return Task.FromResult(dir.Directories.Keys.Select(dir => Path.Combine(absolutepath, dir)).ToArray());
        }

        public Task DeleteFile(string absolutepath)
        {
            var dirname = Path.GetDirectoryName(absolutepath) ?? "";
            var dir = VirtualFS.GetDirectory(dirname) ?? throw new NullReferenceException("Directory not found");
            dir.Files.TryRemove(Path.GetFileName(absolutepath), out _);
            return Task.CompletedTask;
        }

        public async Task<byte[]> ReadFileRegion(string absolutepath, int byteposition, int bytelength)
        {
            using Stream file = await GetFileData(absolutepath);
            byte[] region = new byte[bytelength];
            await file.ReadAsync(region.AsMemory(byteposition, bytelength));
            return region;
        }

        public async Task WriteFileRegion(string absolutepath, int byteposition, byte[] data)
        {
            if (await FileExists(absolutepath))
            {
                var md = await GetFileMetadata(absolutepath);
                if (md.FileHash == null)
                {
                    throw new Exception("Filehash must exist on this FileMetaData object");
                }
                var file = DataStore[md.FileHash]; // existing file
                int modlen = file.Length;
                if (byteposition + data.Length > file.Length)
                {
                    modlen = byteposition + data.Length;
                }
                byte[] modifiedfile = new byte[modlen];
                Array.Copy(file, modifiedfile, file.Length); // Simple copy of old file data to new file
                Array.Copy(data, 0, modifiedfile, byteposition, data.Length); // overwrite specified region
                md.FileHash = StoreDataGetHash(modifiedfile); // File's hash has changed and new hash referrs to modifiedfile
            }
            else if (byteposition == 0)
            {
                await OverwriteOrCreateFile(absolutepath, data);
            }
            else
            {
                throw new Exception("No existing file and nonzero byteposition specified for write");
            }
        }

        private byte[] StoreDataGetHash(byte[] data)
        {
            var datahash = HashTools.GetSHA1Hasher().ComputeHash(data);
            DataStore.Add(datahash, data);
            return datahash;
        }

        public Task WriteOutMetadata(string absolutepath, FileMetadata metadata)
        {
            var dirname = Path.GetDirectoryName(absolutepath) ?? "";
            var node = VirtualFS.GetDirectory(dirname) ?? throw new NullReferenceException("Could not locate directory");
            if (node.Files.ContainsKey(Path.GetFileName(absolutepath)))
            {
                node.Files[Path.GetFileName(absolutepath)] = metadata;
            }
            else if (node.Directories.ContainsKey(Path.GetFileName(absolutepath)))
            {
                node.Directories[Path.GetFileName(absolutepath)].DirMetadata = metadata;
            }
            return Task.CompletedTask;
        }

        // Unlike other public methods of this class, MakeNewFileMetadata and MakeNewDirectoryMetadata
        // are not part of the IFSInterop interface. They are included as convenience methods for
        // use when creating a virtual filesystem
        public static FileMetadata MakeNewFileMetadata(string name, int size=0, byte[]? hash = null) => 
            new(name, DateTime.Now, DateTime.Now, DateTime.Now, FileAttributes.Normal, size, hash);


        public static FileMetadata MakeNewDirectoryMetadata(string name, DateTime? dateTime = null)
        {
            if (dateTime == null)
            {
                dateTime = DateTime.Now;
            }

            return new FileMetadata(name, dateTime.Value, dateTime.Value, dateTime.Value, FileAttributes.Directory, 0, null);
        }

        public async Task<bool> IndexFileExistsAsync(string? bsname, IndexFileType fileType)
        {
            return await FileExists(GetIndexFilePath(bsname, fileType));
        }

        public async Task<byte[]> LoadIndexFileAsync(string? bsname, IndexFileType fileType)
        {
            byte[] data = await ReadAllFileBytes(GetIndexFilePath(bsname, fileType));
            if (Encryptor != null && fileType != IndexFileType.EncryptorKeyFile)
            {
                data = Encryptor.DecryptBytes(data);
            }
            return data;
        }

        /// <summary>
        /// Not actually asynchronous
        /// </summary>
        /// <param name="bsname"></param>
        /// <param name="fileType"></param>
        /// <param name="data"></param>
        public async Task StoreIndexFileAsync(string? bsname, IndexFileType fileType, byte[] data)
        {
            if (Encryptor != null && fileType != IndexFileType.EncryptorKeyFile)
            {
                data = Encryptor.EncryptBytes(data);
            }
            await OverwriteOrCreateFile(GetIndexFilePath(bsname, fileType), data);
        }

        public async Task<byte[]> LoadBlobAsync(byte[] encryptedhash, bool decrypt)
        {
            byte[] data = await ReadAllFileBytes(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(encryptedhash)));
            if (Encryptor != null && decrypt)
            {
                data = Encryptor.DecryptBytes(data);
            }
            return data;
        }

        public async Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] hash, byte[] data)
        {
            if (Encryptor != null)
            {
                data = Encryptor.EncryptBytes(data);
                hash = HashTools.GetSHA1Hasher().ComputeHash(data);
            }
            string relpath = GetBlobRelativePath(hash);
            await OverwriteOrCreateFile(Path.Combine(BlobSaveDirectory, relpath), data);
            return (hash, relpath);
        }

        public async Task DeleteBlobAsync(byte[] hash, string fileId)
        {
            await DeleteFile(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash)));
        }

        private string GetIndexFilePath(string? bsname, IndexFileType fileType)
        {
            string path;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    path = Path.Combine(DstRoot, "index", Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
                    if (bsname == null)
                    {
                        throw new Exception("Backup set name needed to load backup set");
                    }
                    path = Path.Combine(DstRoot, "index", "backupstores", bsname);
                    break;
                case IndexFileType.SettingsFile:
                    path = Path.Combine(DstRoot, Core.SettingsFilename);
                    break;
                case IndexFileType.EncryptorKeyFile:
                    path = Path.Combine(DstRoot, "index", "keyfile");
                    break;
                default:
                    throw new ArgumentException("Unknown IndexFileType");
            }
            return path;
        }

        private static string GetBlobRelativePath(byte[] hash)
        {
            // Save files with names given by their hashes
            // In order to keep the number of files per directory managable,
            // the first two bytes of the hash are stripped and used as 
            // the names of two nested directories into which the file
            // is placed.
            // Ex. hash = 3bc6e94a89 => relpath = 3b/c6/e94a89
            string hashstring = HashTools.ByteArrayToHexViaLookup32(hash);
            string dir1 = hashstring[..2];
            string dir2 = hashstring.Substring(2, 2);
            string fname = hashstring[4..];

            return Path.Combine(dir1, dir2, fname);
        }
    }
}
