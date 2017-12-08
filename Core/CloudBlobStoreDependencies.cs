using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore
{
    class CloudBlobStoreDependencies : IBlobStoreDependencies
    {
        private ICloudInterop CloudInterop { get; set; }

        public CloudBlobStoreDependencies(ICloudInterop cloudinterop)
        {
            CloudInterop = cloudinterop;
        }

        public void DeleteBlob(byte[] hash, string relpath, int byteposition, int bytelength)
        {
            CloudInterop.DeleteFileAsync(HashTools.ByteArrayToHexViaLookup32(hash), relpath);
        }

        public byte[] LoadBlob(string relpath, int byteposition, int bytelength)
        {
            return CloudInterop.DownloadFileAsync(relpath, true).Result;
        }

        public (string relativefilepath, int byteposition) StoreBlob(byte[] hash, byte[] blobdata)
        {
            string hashstring = HashTools.ByteArrayToHexViaLookup32(hash);
            string fileid = CloudInterop.UploadFileAsync(hashstring, hash, blobdata).Result;
            return (fileid, 0);
        }
    }
}
