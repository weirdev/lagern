using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class DiskDstFSInterop : IDstFSInterop
    {
        public DiskDstFSInterop(string dstPath)
        {
            DstPath = dstPath;
        }

        public string DstPath { get; private set; }

        public string BlobSaveDirectory
        {
            get => Path.Combine(DstPath, "blobdata");
        }

        public string IndexDirectory
        {
            get => Path.Combine(DstPath, "index");
        }

        public void DeleteBlobAsync(byte[] hash, string fileId)
        {
            Task.Run(() => File.Delete(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash))));
        }

        public Task<bool> IndexFileExistsAsync(string bsname, IndexFileType fileType)
        {
            return Task.Run(() => File.Exists(GetIndexFilePath(bsname, fileType)));
        }

        public Task<byte[]> LoadBlobAsync(byte[] hash)
        {
            return LoadFileAsync(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash)));
        }

        public Task<byte[]> LoadIndexFileAsync(string bsname, IndexFileType fileType)
        {
            return LoadFileAsync(GetIndexFilePath(bsname, fileType));
        }

        public Task<string> StoreBlobAsync(byte[] hash, byte[] data)
        {
            string relpath = GetBlobRelativePath(hash);
            OverwriteOrCreateFileAsync(Path.Combine(BlobSaveDirectory, relpath), data);
            return Task.Run(() => relpath);
        }

        public void StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data)
        {
            OverwriteOrCreateFileAsync(GetIndexFilePath(bsname, fileType), data);
        }

        private Task<byte[]> LoadFileAsync(string absolutepath)
        {
            return Task.Run(() => File.ReadAllBytes(absolutepath));
        }

        public void OverwriteOrCreateFileAsync(string absolutepath, byte[] data)
        {
            Task.Run(() =>
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

        private string GetIndexFilePath(string bsname, IndexFileType fileType)
        {
            string path;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    path = Path.Combine(IndexDirectory, Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
                    path = Path.Combine(IndexDirectory, "backupstores", bsname);
                    break;
                case IndexFileType.SettingsFile:
                    path = path = Path.Combine(IndexDirectory, Core.SettingsFilename);
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
