using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;

namespace BackupCore
{
    class VirtualFSInterop : IFSInterop
    {

        public MetadataNode VirtualFS { get; set; }
        private BPlusTree<byte[]> DataStore { get; set; }

        VirtualFSInterop(MetadataNode filesystem, BPlusTree<byte[]> datastore)
        {
            VirtualFS = filesystem;
            DataStore = datastore;
        }

        public bool FileExists(string absolutepath) => VirtualFS.GetFile(absolutepath) != null;

        public bool DirectoryExists(string absolutepath) => VirtualFS.GetDirectory(absolutepath) != null;

        public void CreateDirectory(string absolutepath) => VirtualFS.AddDirectory(absolutepath, MakeNewDirectoryMetadata(Path.GetFileName(absolutepath)));

        public byte[] ReadAllFileBytes(string absolutepath) => DataStore.GetRecord(VirtualFS.GetFile(absolutepath).FileHash);

        public FileMetadata GetFileMetadata(string absolutepath)
        {
            var md = VirtualFS.GetFile(absolutepath);
            if (md == null)
            {
                throw new FileNotFoundException();
            }
            return md;
        }

        public Stream GetFileData(string absolutepath) => new MemoryStream(ReadAllFileBytes(absolutepath));

        public string[] GetDirectoryFiles(string absolutepath) => VirtualFS.GetDirectory(absolutepath).Files.Keys.Select(file => Path.Combine(absolutepath, file)).ToArray();

        public void OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata fileMetadata = null)
        {
            var datahash = StoreDataGetHash(data);
            if (fileMetadata == null)
            {
                fileMetadata = MakeNewFileMetadata(absolutepath, datahash);
            }
            VirtualFS.GetDirectory(Path.GetDirectoryName(absolutepath)).Files[Path.GetFileName(absolutepath)] = fileMetadata;
        }

        public string[] GetSubDirectories(string absolutepath) => VirtualFS.GetDirectory(absolutepath).Directories.Keys.Select(dir => Path.Combine(absolutepath, dir)).ToArray();

        private static FileMetadata MakeNewFileMetadata(string name, byte[] hash=null) => new FileMetadata(name, new DateTime(),
                                new DateTime(), new DateTime(), FileAttributes.Normal, 0, hash);


        private static FileMetadata MakeNewDirectoryMetadata(string name) => new FileMetadata(name, new DateTime(),
                                new DateTime(), new DateTime(), FileAttributes.Directory, 0, null);

        public void DeleteFile(string absolutepath) => VirtualFS.GetDirectory(Path.GetDirectoryName(absolutepath)).Files.Remove(Path.GetFileName(absolutepath));

        public byte[] ReadFileRegion(string absolutepath, int byteposition, int bytelength)
        {
            using (Stream file = GetFileData(absolutepath))
            {
                byte[] region = new byte[bytelength];
                file.Read(region, byteposition, bytelength);
                return region;
            }
        }

        public void WriteFileRegion(string absolutepath, int byteposition, byte[] data)
        {
            if (FileExists(absolutepath))
            {
                var md = GetFileMetadata(absolutepath);
                var file = DataStore.GetRecord(md.FileHash); // existing file
                int modlen = file.Length;
                if (byteposition + data.Length > file.Length)
                {
                    modlen = byteposition + data.Length;
                }
                byte[] modifiedfile = new byte[modlen];
                Array.Copy(file, modifiedfile, file.Length); // Simple copy of old file data to new file
                Array.Copy(data, 0, modifiedfile, byteposition, data.Length); // overwrite specified region
                md.FileHash = StoreDataGetHash(modifiedfile); // File's hash has changed and new hash referrs to modifiedfile
            }
            else if (byteposition == 0)
            {
                OverwriteOrCreateFile(absolutepath, data);
            }
            else
            {
                throw new Exception("No existing file and nonzero byteposition specified for write");
            }
        }

        private byte[] StoreDataGetHash(byte[] data)
        {
            var datahash = HashTools.GetSHA1Hasher().ComputeHash(data);
            DataStore.AddHash(datahash, data);
            return datahash;
        }

        public void WriteOutMetadata(string absolutepath, FileMetadata metadata)
        {
            var node = VirtualFS.GetDirectory(Path.GetDirectoryName(absolutepath));
            if (node.Files.ContainsKey(Path.GetFileName(absolutepath)))
            {
                node.Files[Path.GetFileName(absolutepath)] = metadata;
            }
            else if(node.Directories.ContainsKey(Path.GetFileName(absolutepath)))
            {
                node.Directories[Path.GetFileName(absolutepath)].DirMetadata = metadata;
            }
        }
    }
}
