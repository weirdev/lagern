using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public interface ICloudInterop
    {
        Task<string> UploadFileAsync(string file, byte[] data);
        Task<string> UploadFileAsync(string file, byte[] hash, byte[] data);
        Task<byte[]> DownloadFileAsync(string fileNameOrId, bool fileid = false);
        void DeleteFileAsync(string filename, string fileid);
        Task<bool> FileExistsAsync(string file);
    }
}
