using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        byte[] LoadBlob(byte[] hash);

        void DeleteBlob(byte[] hash, string fileId);
        
        string StoreBlob(byte[] hash, byte[] blobdata);
    }
}
