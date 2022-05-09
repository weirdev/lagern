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

        public async Task DeleteBlob(byte[] encryptedHash, string fileId)
        {
            await DstFSInterop.DeleteBlobAsync(encryptedHash, fileId);
        }

        public async Task<byte[]> LoadBlob(byte[] encryptedhash, bool decrypt)
        {
            return await DstFSInterop.LoadBlobAsync(encryptedhash, decrypt);
        }

        public async Task<(byte[] encryptedHash, string fileId)> StoreBlob(byte[] hash, byte[] blobdata)
        {
            return await DstFSInterop.StoreBlobAsync(hash, blobdata);
        }
    }
}
