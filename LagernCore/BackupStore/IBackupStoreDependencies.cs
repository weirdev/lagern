using LagernCore.Models;

namespace BackupCore
{
    public interface IBackupStoreDependencies
    {
        BlobStore Blobs { get; }

        byte[] LoadBackupSetData(BackupSetReference backupsetname);

        void StoreBackupSetData(BackupSetReference backupsetname, byte[] bsdata);
    }
}
