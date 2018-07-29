﻿using System;
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
        void StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data);
        Task<byte[]> LoadBlobAsync(byte[] hash);
        Task<string> StoreBlobAsync(byte[] hash, byte[] data);
        void DeleteBlobAsync(byte[] hash, string fileId);
    }
}
