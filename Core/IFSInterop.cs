using System;
using System.Collections.Generic;
using System.Text;


namespace BackupCore
{
    public interface IFSInterop
    {
        bool FileExists(string absolutepath);

        bool DirectoryExists(string absolutepath);

        void CreateDirectory(string absolutepath);

        byte[] ReadAllFileBytes(string absolutepath);

        FileMetadata GetFileMetadata(string absolutepath);

        System.IO.Stream GetFileData(string absolutepath);

        string[] GetDirectoryFiles(string absolutepath);

        void OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata fileMetadata = null);

        string[] GetSubDirectories(string absolutepath);

        void DeleteFile(string absolutepath);

        byte[] ReadFileRegion(string absolutepath, int byteposition, int bytelength);

        void WriteFileRegion(string absolutepath, int byteposition, byte[] data);

        void WriteOutMetadata(string absolutepath, FileMetadata metadata);
    }
}
