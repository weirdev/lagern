using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    public interface IBlobStoreDependencies
    {
        void StoreBlobIndex();

        byte[] LoadBlob(string relpath, int byteposition, int bytelength);

        byte[] DeleteBlob(string relpath, int byteposition, int bytelength);
    }
}
