using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BlobStoreDependencies : IBlobStoreDependencies
    {
        private IDstFSInterop DstFSInterop { get; set; }

        public BlobStoreDependencies(IDstFSInterop fsinterop)
        {
            DstFSInterop = fsinterop;
        }

        public void DeleteBlob(byte[] encryptedHash, string fileId)
        {
            DstFSInterop.DeleteBlobAsync(encryptedHash, fileId).Wait();
        }

        public byte[] LoadBlob(byte[] encryptedhash, bool decrypt)
        {
            return DstFSInterop.LoadBlobAsync(encryptedhash, decrypt).Result;
        }

        public (byte[] encryptedHash, string fileId) StoreBlob(byte[] hash, byte[] blobdata)
        {
            return DstFSInterop.StoreBlobAsync(hash, blobdata).Result;
        }
    }
}
