using System;
using System.Collections.Generic;
using System.Text;
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

        Task<bool> IndexFileExistsAsync(string bsname, IndexFileType fileType);
        Task<byte[]> LoadIndexFileAsync(string bsname, IndexFileType fileType);
        Task StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data);
        Task<byte[]> LoadBlobAsync(byte[] encryptedHash);
        Task<(byte[] encryptedHash, string fileId)> StoreBlobAsync(byte[] rawHash, byte[] data);
        Task DeleteBlobAsync(byte[] encryptedHash, string fileId);
    }
}
