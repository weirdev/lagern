using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class DiskDstFSInterop : IDstFSInterop
    {
        public static IDstFSInterop InitializeNew(string dstpath, string? password=null)
        {
            DiskDstFSInterop diskDstFSInterop = new DiskDstFSInterop(dstpath);
            if (password != null)
            {
                AesHelper encryptor = AesHelper.CreateFromPassword(password);
                byte[] keyfile = encryptor.CreateKeyFile();
                diskDstFSInterop.StoreIndexFileAsync(null, IndexFileType.EncryptorKeyFile, keyfile).Wait();
                diskDstFSInterop.Encryptor = encryptor;
            }
            return diskDstFSInterop;
        }

        public static IDstFSInterop Load(string dstpath, string? password=null)
        {
            DiskDstFSInterop diskDstFSInterop = new DiskDstFSInterop(dstpath);
            if (password != null)
            {
                byte[] keyfile = diskDstFSInterop.LoadIndexFileAsync(null, IndexFileType.EncryptorKeyFile).Result;
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

        private AesHelper? Encryptor { get; set; }

        public string BlobSaveDirectory
        {
            get => Path.Combine(DstPath, "blobdata");
        }
        
        public string IndexDirectory
        {
            get => Path.Combine(DstPath, "index");
        }

        public Task DeleteBlobAsync(byte[] hash, string fileId)
        {
            return Task.Run(() => File.Delete(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash))));
        }

        public Task<bool> IndexFileExistsAsync(string? bsname, IndexFileType fileType)
        {
            return Task.Run(() => File.Exists(GetIndexFilePath(bsname, fileType)));
        }

        public async Task<byte[]> LoadBlobAsync(byte[] hash)
        {
            byte[] data = await LoadFileAsync(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash)));
            if (Encryptor != null)
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

        public Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] hash, byte[] data)
        {
            if (Encryptor != null)
            {
                data = Encryptor.EncryptBytes(data);
                hash = HashTools.GetSHA1Hasher().ComputeHash(data);
            }
            string relpath = GetBlobRelativePath(hash);
            OverwriteOrCreateFileAsync(Path.Combine(BlobSaveDirectory, relpath), data);
            return Task.Run(() => (hash, relpath));
        }

        public Task StoreIndexFileAsync(string? bsname, IndexFileType fileType, byte[] data)
        {
            // Never Encrypt Key File
            if (Encryptor != null && fileType != IndexFileType.EncryptorKeyFile)
            {
                data = Encryptor.EncryptBytes(data);
            }
            return OverwriteOrCreateFileAsync(GetIndexFilePath(bsname, fileType), data);
        }

        private Task<byte[]> LoadFileAsync(string absolutepath)
        {
            return Task.Run(() => File.ReadAllBytes(absolutepath));
        }

        public Task OverwriteOrCreateFileAsync(string absolutepath, byte[] data)
        {
            return Task.Run(() =>
            {
                Directory.CreateDirectory(Path.GetDirectoryName(absolutepath));

                // The more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
                using (FileStream writer = new FileStream(absolutepath, FileMode.OpenOrCreate))
                {
                    writer.Write(data, 0, data.Length);
                    // Flush the writer in order to get a correct stream position for truncating
                    writer.Flush();
                    // Set the stream length to the current position in order to truncate leftover data in original file
                    writer.SetLength(writer.Position);
                }
            });
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

        private string GetBlobRelativePath(byte[] hash)
        {
            // Save files with names given by their hashes
            // In order to keep the number of files per directory managable,
            // the first two bytes of the hash are stripped and used as 
            // the names of two nested directories into which the file
            // is placed.
            // Ex. hash = 3bc6e94a89 => relpath = 3b/c6/e94a89
            string hashstring = HashTools.ByteArrayToHexViaLookup32(hash);
            string dir1 = hashstring.Substring(0, 2);
            string dir2 = hashstring.Substring(2, 2);
            string fname = hashstring.Substring(4);

            return Path.Combine(dir1, dir2, fname);
        }
    }
}
