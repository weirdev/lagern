using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    class DiskFSInterop : IFSInterop
    {
        public bool DirectoryExists(string absolutepath) => Directory.Exists(absolutepath);

        public bool FileExists(string absolutepath) => File.Exists(absolutepath);

        public void CreateDirectory(string absolutepath) => Directory.CreateDirectory(absolutepath);

        public byte[] ReadAllFileBytes(string absolutepath) => File.ReadAllBytes(absolutepath);

        public FileMetadata GetFileMetadata(string absolutepath) => new FileMetadata(absolutepath);

        public Stream GetFileData(string absolutepath) => File.OpenRead(absolutepath);

        public string[] GetDirectoryFiles(string absolutepath) => Directory.GetFiles(absolutepath);

        public void OverwriteOrCreateFile(string absolutepath, byte[] data)
        {
            using (FileStream fs = new FileStream(absolutepath, FileMode.Create, FileAccess.Write))
            {
                using (BinaryWriter writer = new BinaryWriter(fs))
                {
                    writer.Write(data);
                }
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
