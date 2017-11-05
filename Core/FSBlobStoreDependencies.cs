using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    class FSBlobStoreDependencies : IBlobStoreDependencies
    {
        // NOTE: TransferBlobAndReferences will need to be updated if blob 
        // addressing is changed to be no longer 1 blob per file and 
        // all blobs in single directory
        public string BlobSaveDirectory { get; set; }

        public FSBlobStoreDependencies(string blobsavedir)
        {
            BlobSaveDirectory = blobsavedir;
        }

        public void DeleteBlob(string relpath, int byteposition, int bytelength)
        {
            File.Delete(Path.Combine(BlobSaveDirectory, relpath));
        }

        public byte[] LoadBlob(string relpath, int byteposition, int bytelength)
        {
            try
            {
                FileStream blobstream = File.OpenRead(Path.Combine(BlobSaveDirectory, relpath));
                byte[] buffer = new byte[blobstream.Length];
                blobstream.Read(buffer, byteposition, bytelength);
                blobstream.Close();
                return buffer;
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to read blob");
                throw;
            }
        }

        public void StoreBlob(byte[] blobdata, string relpath, int byteposition, int bytelength)
        {
            string path = Path.Combine(BlobSaveDirectory, relpath);
            try
            {
                using (FileStream writer = File.OpenWrite(path))
                {
                    writer.Seek(byteposition, SeekOrigin.Begin);
                    writer.Write(blobdata, 0, blobdata.Length);
                    writer.Flush();
                    writer.Close();
                }
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to write blob");
                throw;
            }
        }
    }
}
