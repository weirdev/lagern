using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        byte[] LoadBlob(byte[] hash);

        void DeleteBlob(byte[] hash, string fileId);

        (byte[] encryptedHash, string fileId) StoreBlob(byte[] hash, byte[] blobdata);
    }
}
