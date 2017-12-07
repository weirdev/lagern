using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    class BackblazeBlobStoreDependencies : IBlobStoreDependencies
    {
        private BackblazeInterop BBInterop { get; set; }

        public BackblazeBlobStoreDependencies(BackblazeInterop bbinterop)
        {
            BBInterop = bbinterop;
        }

        public void DeleteBlob(byte[] hash, string relpath, int byteposition, int bytelength)
        {
            BBInterop.DeleteFile(HashTools.ByteArrayToHexViaLookup32(hash), relpath);
        }

        public byte[] LoadBlob(string relpath, int byteposition, int bytelength)
        {
            return BBInterop.DownloadFile(relpath, true).Result;
        }

        public (string relativefilepath, int byteposition) StoreBlob(byte[] hash, byte[] blobdata)
        {
            string hashstring = HashTools.ByteArrayToHexViaLookup32(hash);
            string fileid = BBInterop.UploadFileAsync(hashstring, blobdata).Result;
            return (fileid, 0);
        }
    }
}
