using LagernCore.Models;

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

        public byte[] LoadBackupSetData(BackupSetReference backupsetname)
        {
            return DstFSInterop.LoadIndexFileAsync(backupsetname.StringRepr(), IndexFileType.BackupSet).Result;
        }

        public void StoreBackupSetData(BackupSetReference backupsetname, byte[] bsdata)
        {
            DstFSInterop.StoreIndexFileAsync(backupsetname.StringRepr(), IndexFileType.BackupSet, bsdata).Wait();
        }
    }
}
