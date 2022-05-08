using System;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    public class DiskDstFSInterop : IDstFSInterop
    {
        public static async Task<IDstFSInterop> InitializeNew(string dstpath, string? password=null)
        {
            DiskDstFSInterop diskDstFSInterop = new(dstpath);
            if (password != null)
            {
                AesHelper encryptor = AesHelper.CreateFromPassword(password);
                byte[] keyfile = encryptor.CreateKeyFile();
                await diskDstFSInterop.StoreIndexFileAsync(null, IndexFileType.EncryptorKeyFile, keyfile);
                diskDstFSInterop.Encryptor = encryptor;
            }
            return diskDstFSInterop;
        }

        public static async Task<IDstFSInterop> Load(string dstpath, string? password=null)
        {
            DiskDstFSInterop diskDstFSInterop = new(dstpath);
            if (password != null)
            {
                byte[] keyfile = await diskDstFSInterop.LoadIndexFileAsync(null, IndexFileType.EncryptorKeyFile);
                AesHelper encryptor = AesHelper.CreateFromKeyFile(keyfile, password);
                diskDstFSInterop.Encryptor = encryptor;
            }
            return diskDstFSInterop;
        }

        private DiskDstFSInterop(string dstPath)
        {
            DstPath = dstPath;
        }

        public string DstPath { get; private set; }

        public AesHelper? Encryptor { get; private set; }

        public string BlobSaveDirectory
        {
            get => Path.Combine(DstPath, "blobdata");
        }
        
        public string IndexDirectory
        {
            get => Path.Combine(DstPath, "index");
        }

        public async Task DeleteBlobAsync(byte[] hash, string fileId)
        {
            await Task.Run(() => File.Delete(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash))));
        }

        public async Task<bool> IndexFileExistsAsync(string? bsname, IndexFileType fileType)
        {
            return await Task.Run(() => File.Exists(GetIndexFilePath(bsname, fileType)));
        }

        public async Task<byte[]> LoadBlobAsync(byte[] hash, bool decrypt)
        {
            byte[] data = await LoadFileAsync(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash)));
            if (Encryptor != null && decrypt)
            {
                data = Encryptor.DecryptBytes(data);
            }
            return data;
        }

        public async Task<byte[]> LoadIndexFileAsync(string? bsname, IndexFileType fileType)
        {
            byte[] data = await LoadFileAsync(GetIndexFilePath(bsname, fileType));
            if (Encryptor != null && fileType != IndexFileType.EncryptorKeyFile)
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
            await OverwriteOrCreateFileAsync(Path.Combine(BlobSaveDirectory, relpath), data);
            return (hash, relpath);
        }

        public async Task StoreIndexFileAsync(string? bsname, IndexFileType fileType, byte[] data)
        {
            // Never Encrypt Key File
            if (Encryptor != null && fileType != IndexFileType.EncryptorKeyFile)
            {
                data = Encryptor.EncryptBytes(data);
            }
            await OverwriteOrCreateFileAsync(GetIndexFilePath(bsname, fileType), data);
        }

        private static Task<byte[]> LoadFileAsync(string absolutepath)
        {
            return Task.Run(() => File.ReadAllBytes(absolutepath));
        }

        public static async Task OverwriteOrCreateFileAsync(string absolutepath, byte[] data)
        {
            string? path = Path.GetDirectoryName(absolutepath);
            if (path == null)
            {
                throw new ArgumentException("Absolute path must have a directory"); // TODO: Do we need to require this? Or just not create directory if none given.
            }
            Directory.CreateDirectory(path);

            // The more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
            using FileStream writer = new(absolutepath, FileMode.OpenOrCreate);
            await writer.WriteAsync(data);
            // Flush the writer in order to get a correct stream position for truncating
            writer.Flush();
            // Set the stream length to the current position in order to truncate leftover data in original file
            writer.SetLength(writer.Position);
        }

        private string GetIndexFilePath(string? bsname, IndexFileType fileType)
        {
            string path;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    path = Path.Combine(IndexDirectory, Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
                    if (bsname == null)
                    {
                        throw new Exception("Backup set name must not be null when loading backup set");
                    }
                    path = Path.Combine(IndexDirectory, "backupstores", bsname);
                    break;
                case IndexFileType.SettingsFile:
                    path = Path.Combine(IndexDirectory, Core.SettingsFilename);
                    break;
                case IndexFileType.EncryptorKeyFile:
                    path = Path.Combine(IndexDirectory, "keyfile");
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
