using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public class BlobStoreDependencies : IBlobStoreDependencies
    {
        private IDstFSInterop DstFSInterop { get; set; }

        public BlobStoreDependencies(IDstFSInterop fsinterop)
        {
            DstFSInterop = fsinterop;
        }

        public void DeleteBlob(byte[] hash, string fileId)
        {
            DstFSInterop.DeleteBlobAsync(hash, fileId);
        }

        public byte[] LoadBlob(byte[] hash)
        {
            return DstFSInterop.LoadBlobAsync(hash).Result;
        }

        public (byte[] encryptedHash, string fileId) StoreBlob(byte[] hash, byte[] blobdata)
        {
            return DstFSInterop.StoreBlobAsync(hash, blobdata).Result;
        }
    }
}
