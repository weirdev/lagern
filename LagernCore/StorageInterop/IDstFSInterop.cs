using System.Threading.Tasks;

namespace BackupCore
{
    public interface IDstFSInterop
    {
        /*
        Task<string> StoreFileAsync(string file, byte[] data);
        Task<string> StoreFileAsync(string file, byte[] hash, byte[] data);
        Task<byte[]> LoadFileAsync(string fileNameOrId, bool fileid = false);
        void DeleteFileAsync(string filename, string fileid);
        Task<bool> FileExistsAsync(string file);
        */

        AesHelper? Encryptor { get; }

        Task<bool> IndexFileExistsAsync(string? bsname, IndexFileType fileType);

        Task<byte[]> LoadIndexFileAsync(string? bsname, IndexFileType fileType);

        Task StoreIndexFileAsync(string? bsname, IndexFileType fileType, byte[] data);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="encryptedHash"></param>
        /// <param name="decrypt">Decrypt if encrypted</param>
        /// <returns></returns>
        Task<byte[]> LoadBlobAsync(byte[] encryptedHash, bool decrypt=true);

        Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] rawHash, byte[] data);

        Task DeleteBlobAsync(byte[] encryptedHash, string fileId);
    }
}
