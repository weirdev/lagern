using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    public class DiskFSInterop : IFSInterop
    {
        public bool DirectoryExists(string absolutepath) => Directory.Exists(absolutepath);

        public bool FileExists(string absolutepath) => File.Exists(absolutepath);

        public void CreateDirectoryIfNotExists(string absolutepath) => Directory.CreateDirectory(absolutepath);

        public byte[] ReadAllFileBytes(string absolutepath) => File.ReadAllBytes(absolutepath);

        public FileMetadata GetFileMetadata(string absolutepath) => new FileMetadata(absolutepath);

        public Stream GetFileData(string absolutepath) => new FileStream(absolutepath, FileMode.OpenOrCreate);

        public string[] GetDirectoryFiles(string absolutepath) => Directory.GetFiles(absolutepath);

        public void OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata fileMetadata = null)
        {
            // The more obvious FileMode.Create causes issues with hidden files, so open, overwrite, then truncate
            using (FileStream writer = new FileStream(absolutepath, FileMode.OpenOrCreate))
            {
                writer.Write(data, 0, data.Length);
                // Flush the writer in order to get a correct stream position for truncating
                writer.Flush();
                // Set the stream length to the current position in order to truncate leftover data in original file
                writer.SetLength(writer.Position);
            }
            if (fileMetadata != null)
            {
                WriteOutMetadata(absolutepath, fileMetadata);
            }
        }

        public void WriteOutMetadata(string absolutepath, FileMetadata fileMetadata)
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
        }

        public string[] GetSubDirectories(string absolutepath) => Directory.GetDirectories(absolutepath);

        public void DeleteFile(string absolutepath) => File.Delete(absolutepath);

        public byte[] ReadFileRegion(string absolutepath, int byteposition, int bytelength)
        {
            using (Stream file = GetFileData(absolutepath))
            {
                byte[] region = new byte[bytelength];
                file.Read(region, byteposition, bytelength);
                return region;
            }
        }

        public void WriteFileRegion(string absolutepath, int byteposition, byte[] data)
        {
            using (FileStream writer = File.OpenWrite(absolutepath))
            {
                writer.Seek(byteposition, SeekOrigin.Begin);
                writer.Write(data, 0, data.Length);
                writer.Flush();
                writer.Close();
            }
        }
    }
}
