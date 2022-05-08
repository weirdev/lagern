using System;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    public class DiskFSInterop : IFSInterop
    {
        public Task<bool> DirectoryExists(string absolutepath) => Task.Run(() => Directory.Exists(absolutepath));

        public Task<bool> FileExists(string absolutepath) => Task.Run(() => File.Exists(absolutepath));

        public Task CreateDirectoryIfNotExists(string absolutepath) => Task.Run(() => Directory.CreateDirectory(absolutepath));

        public async Task<byte[]> ReadAllFileBytes(string absolutepath) => await File.ReadAllBytesAsync(absolutepath);

        public Task<FileMetadata> GetFileMetadata(string absolutepath) => Task.Run(() => new FileMetadata(absolutepath));

        public Task<Stream> GetFileData(string absolutepath) => Task.Run(() => (Stream) new FileStream(absolutepath, FileMode.OpenOrCreate));

        public Task<string[]> GetDirectoryFiles(string absolutepath) => Task.Run(() => Directory.GetFiles(absolutepath));

        public async Task OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata? fileMetadata = null)
        {
            // The more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
            using (FileStream writer = new(absolutepath, FileMode.OpenOrCreate))
            {
                await writer.WriteAsync(data);
                // Flush the writer in order to get a correct stream position for truncating
                writer.Flush();
                // Set the stream length to the current position in order to truncate leftover data in original file
                writer.SetLength(writer.Position);
            }
            if (fileMetadata != null)
            {
                await WriteOutMetadata(absolutepath, fileMetadata);
            }
        }

        public Task WriteOutMetadata(string absolutepath, FileMetadata fileMetadata)
        {
            FileSystemInfo fi = new FileInfo(absolutepath);
            if ((fileMetadata.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                // For some reason cannot assign back to a FileInfo of a directory
                fi = new DirectoryInfo(absolutepath);
            }
            fi.LastAccessTimeUtc = fileMetadata.DateAccessedUTC;
            fi.LastWriteTimeUtc = fileMetadata.DateModifiedUTC;
            fi.CreationTimeUtc = fileMetadata.DateCreatedUTC;
            if (fi.Attributes != 0)
            {
                fi.Attributes = fileMetadata.Attributes;
            }
            return Task.CompletedTask;
        }

        public Task<string[]> GetSubDirectories(string absolutepath) => Task.Run(() => Directory.GetDirectories(absolutepath));

        public Task DeleteFile(string absolutepath) => Task.Run(() => File.Delete(absolutepath));

        public async Task<byte[]> ReadFileRegion(string absolutepath, int byteposition, int bytelength)
        {
            using Stream file = await GetFileData(absolutepath);
            byte[] region = new byte[bytelength];
            await file.ReadAsync(region.AsMemory(byteposition, bytelength));
            return region;
        }

        public async Task WriteFileRegion(string absolutepath, int byteposition, byte[] data)
        {
            using FileStream writer = File.OpenWrite(absolutepath);
            writer.Seek(byteposition, SeekOrigin.Begin);
            await writer.WriteAsync(data);
            writer.Flush();
            writer.Close();
        }
    }
}
