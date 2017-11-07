using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        byte[] LoadBlob(string relpath, int byteposition, int bytelength);

        void DeleteBlob(string relpath, int byteposition, int bytelength);

        void StoreBlob(byte[] blobdata, string relpath, int byteposition);
    }
}
