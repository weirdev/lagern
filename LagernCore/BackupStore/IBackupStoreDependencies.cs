using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBackupStoreDependencies
    {
        BlobStore Blobs { get; }

        byte[] LoadBackupSetData(string backupsetname);

        void StoreBackupSetData(string backupsetname, byte[] bsdata);
    }
}
