using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    public class FSBlobStoreDependencies : IBlobStoreDependencies
    {
        // NOTE: TransferBlobAndReferences will need to be updated if blob 
        // addressing is changed to be no longer 1 blob per file and 
        // all blobs in single directory
        public string BlobSaveDirectory { get; set; }

        private IFSInterop FSInterop { get; set; }

        public FSBlobStoreDependencies(IFSInterop fsinterop, string blobsavedir)
        {
            BlobSaveDirectory = blobsavedir;
            FSInterop = fsinterop;
        }

        public void DeleteBlob(string relpath, int byteposition, int bytelength)
        {
            FSInterop.DeleteFile(Path.Combine(BlobSaveDirectory, relpath));
        }

        public byte[] LoadBlob(string relpath, int byteposition, int bytelength)
        {
            try
            {
                return FSInterop.ReadFileRegion(Path.Combine(BlobSaveDirectory, relpath), byteposition, bytelength);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to read blob");
                throw;
            }
        }

        public void StoreBlob(byte[] blobdata, string relpath, int byteposition)
        {
            string path = Path.Combine(BlobSaveDirectory, relpath);
            try
            {
                FSInterop.WriteFileRegion(path, byteposition, blobdata);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to write blob");
                throw;
            }
        }
    }
}
