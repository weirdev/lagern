using LagernCore.Models;
using System.Threading.Tasks;

namespace BackupCore
{
    class BackupStoreDependencies : IBackupStoreDependencies
    {
        public BlobStore Blobs { get; set; }

        private IDstFSInterop DstFSInterop { get; set; }

        public BackupStoreDependencies(IDstFSInterop cloudinterop, BlobStore blobs)
        {
            DstFSInterop = cloudinterop;
            Blobs = blobs;
        }

        public async Task<byte[]> LoadBackupSetData(BackupSetReference backupsetname)
        {
            return await DstFSInterop.LoadIndexFileAsync(backupsetname.StringRepr(), IndexFileType.BackupSet);
        }

        public async Task StoreBackupSetData(BackupSetReference backupsetname, byte[] bsdata)
        {
            await DstFSInterop.StoreIndexFileAsync(backupsetname.StringRepr(), IndexFileType.BackupSet, bsdata);
        }
    }
}
