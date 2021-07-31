using System.Collections.Generic;
using System.IO;

namespace BackupCore
{
    public interface ICoreSrcDependencies
    {
        FileMetadata GetFileMetadata(string relpath);

        IEnumerable<string> GetDirectoryFiles(string relpath);

        IEnumerable<string> GetSubDirectories(string relpath);

        Stream GetFileData(string relpath);

        void OverwriteOrCreateFile(string path, byte[] data, FileMetadata? fileMetadata = null, bool absolutepath = false);

        void DeleteFile(string path, bool absolutepath = false);

        void CreateDirectory(string path, bool absolutepath = false);

        void WriteOutMetadata(string path, FileMetadata metadata, bool absolutepath = false);

        string ReadSetting(BackupSetting key);

        Dictionary<BackupSetting, string> ReadSettings();

        void WriteSetting(BackupSetting key, string value);

        void ClearSetting(BackupSetting key);
    }
}
