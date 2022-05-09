using System.Threading.Tasks;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        Task<byte[]> LoadBlob(byte[] hash, bool decrypt=true);

        Task DeleteBlob(byte[] hash, string fileId);

        Task<(byte[] encryptedHash, string fileId)> StoreBlob(byte[] hash, byte[] blobdata);
    }
}
