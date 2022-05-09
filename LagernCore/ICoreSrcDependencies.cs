using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace BackupCore
{
    public interface ICoreSrcDependencies
    {
        Task<FileMetadata> GetFileMetadata(string relpath);

        Task<IEnumerable<string>> GetDirectoryFiles(string relpath);

        Task<IEnumerable<string>> GetSubDirectories(string relpath);

        Task<Stream> GetFileData(string relpath);

        Task OverwriteOrCreateFile(string path, byte[] data, FileMetadata? fileMetadata = null, bool absolutepath = false);

        Task DeleteFile(string path, bool absolutepath = false);

        Task CreateDirectory(string path, bool absolutepath = false);

        Task WriteOutMetadata(string path, FileMetadata metadata, bool absolutepath = false);

        Task<string> ReadSetting(BackupSetting key);

        Task<Dictionary<BackupSetting, string>> ReadSettings();

        Task WriteSetting(BackupSetting key, string value);

        Task ClearSetting(BackupSetting key);
    }
}
