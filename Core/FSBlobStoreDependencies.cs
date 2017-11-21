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

        public (string relativefilepath, int byteposition) StoreBlob(byte[] blobhash, byte[] blobdata)
        {
            // Save files with names given by their hashes
            // In order to keep the number of files per directory managable,
            // the first two bytes of the hash are stripped and used as 
            // the names of two nested directories into which the file
            // is placed.
            // Ex. hash = 3bc6e94a89 => relpath = 3b/c6/e94a89
            string hashstring = HashTools.ByteArrayToHexViaLookup32(blobhash);
            string dir1 = hashstring.Substring(0, 2);
            string dir2 = hashstring.Substring(2, 2);
            string fname = hashstring.Substring(4);

            string relpath = Path.Combine(dir1, dir2, fname);
            string dir1path = Path.Combine(BlobSaveDirectory, dir1);
            string dir2path = Path.Combine(dir1path, dir2);
            string path = Path.Combine(BlobSaveDirectory, relpath);
            try
            {
                FSInterop.CreateDirectoryIfNotExists(dir1path);
                FSInterop.CreateDirectoryIfNotExists(dir2path);
                // NOTE: Right now every file is saved seperately
                // (byteposition is always 0). In the future a packfile 
                // may be added, so we continue specifying the byteposition
                FSInterop.WriteFileRegion(path, 0, blobdata);
                return (relpath, 0);
            }
            catch (Exception)
            {
                Console.WriteLine("Failed to write blob");
                throw;
            }
        }
    }
}
