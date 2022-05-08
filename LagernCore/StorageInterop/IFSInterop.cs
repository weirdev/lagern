using System.Threading.Tasks;

namespace BackupCore
{
    public interface IFSInterop
    {
        Task<bool> FileExists(string absolutepath);

        Task<bool> DirectoryExists(string absolutepath);

        Task CreateDirectoryIfNotExists(string absolutepath);

        Task<byte[]> ReadAllFileBytes(string absolutepath);

        Task<FileMetadata> GetFileMetadata(string absolutepath);

        Task<System.IO.Stream> GetFileData(string absolutepath);

        Task<string[]> GetDirectoryFiles(string absolutepath);

        Task OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata? fileMetadata = null);

        Task<string[]> GetSubDirectories(string absolutepath);

        Task DeleteFile(string absolutepath);

        Task<byte[]> ReadFileRegion(string absolutepath, int byteposition, int bytelength);

        Task WriteFileRegion(string absolutepath, int byteposition, byte[] data);

        Task WriteOutMetadata(string absolutepath, FileMetadata metadata);
    }
}
