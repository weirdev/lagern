using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        byte[] LoadBlob(byte[] hash, bool decrypt=true);

        void DeleteBlob(byte[] hash, string fileId);

        (byte[] encryptedHash, string fileId) StoreBlob(byte[] hash, byte[] blobdata);
    }
}
