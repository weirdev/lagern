using LagernCore.Models;
using System.Threading.Tasks;

namespace BackupCore
{
    public interface IBackupStoreDependencies
    {
        BlobStore Blobs { get; }

        Task<byte[]> LoadBackupSetData(BackupSetReference backupsetname);

        Task StoreBackupSetData(BackupSetReference backupsetname, byte[] bsdata);
    }
}
