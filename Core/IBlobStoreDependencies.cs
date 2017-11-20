using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        byte[] LoadBlob(string relpath, int byteposition, int bytelength);

        void DeleteBlob(string relpath, int byteposition, int bytelength);
        
        (string relativefilepath, int byteposition) StoreBlob(byte[] hash, byte[] blobdata);
    }
}
