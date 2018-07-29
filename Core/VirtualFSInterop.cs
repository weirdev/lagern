using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

namespace BackupCore
{
    public class VirtualFSInterop : IFSInterop, IDstFSInterop
    {
        public MetadataNode VirtualFS { get; set; }
        private IDictionary<byte[], byte[]> DataStore { get; set; }

        public string DstRoot { get; private set; }

        public VirtualFSInterop(MetadataNode filesystem, BPlusTree<byte[]> datastore, string dstroot)
        {
            VirtualFS = filesystem;
            DataStore = datastore;
            DstRoot = dstroot;
        }

        public bool FileExists(string absolutepath) => VirtualFS.GetFile(absolutepath) != null;

        public bool DirectoryExists(string absolutepath) => VirtualFS.GetDirectory(absolutepath) != null;

        public void CreateDirectoryIfNotExists(string absolutepath)
        {
            lock (this)
            {
                if (!DirectoryExists(absolutepath))
                {
                    VirtualFS.AddDirectory(Path.GetDirectoryName(absolutepath), MakeNewDirectoryMetadata(Path.GetFileName(absolutepath)));
                }
            }
        }

        public string BlobSaveDirectory
        {
            get => Path.Combine(DstRoot, "blobdata");
        }

        public byte[] ReadAllFileBytes(string absolutepath) => DataStore[VirtualFS.GetFile(absolutepath).FileHash];

        public FileMetadata GetFileMetadata(string absolutepath)
        {
            var md = VirtualFS.GetFile(absolutepath);
            if (md == null)
            {
                md = VirtualFS.GetDirectory(absolutepath).DirMetadata;
                if (md == null)
                {
                    throw new FileNotFoundException();
                }
            }
            return md;
        }

        public Stream GetFileData(string absolutepath) => new MemoryStream(ReadAllFileBytes(absolutepath));

        public string[] GetDirectoryFiles(string absolutepath) => VirtualFS.GetDirectory(absolutepath).Files.Keys.Select(file => Path.Combine(absolutepath, file)).ToArray();

        public void OverwriteOrCreateFile(string absolutepath, byte[] data, FileMetadata fileMetadata = null)
        {
            var root = VirtualFS;
            foreach (var dir in absolutepath.Split(Path.DirectorySeparatorChar))
            {
                if (!root.HasDirectory(dir))
                {
                    root = root.AddDirectory(MakeNewDirectoryMetadata(dir));
                }
                else
                {
                    root = root.GetDirectory(dir);
                }
            }
            var datahash = StoreDataGetHash(data);
            if (fileMetadata == null)
            {
                fileMetadata = MakeNewFileMetadata(Path.GetFileName(absolutepath), data.Length, datahash);
            }
            VirtualFS.GetDirectory(Path.GetDirectoryName(absolutepath)).Files[Path.GetFileName(absolutepath)] = fileMetadata;
        }

        public string[] GetSubDirectories(string absolutepath) => VirtualFS.GetDirectory(absolutepath).Directories.Keys.Select(dir => Path.Combine(absolutepath, dir)).ToArray();

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
                var file = DataStore[md.FileHash]; // existing file
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
            DataStore.Add(datahash, data);
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

        // Unlike other public methods of this class, MakeNewFileMetadata and MakeNewDirectoryMetadata
        // are not part of the IFSInterop interface. They are included as convenience methods for
        // use when creating a virtual filesystem
        public static FileMetadata MakeNewFileMetadata(string name, int size=0, byte[] hash = null) => new FileMetadata(name, new DateTime(),
                                new DateTime(), new DateTime(), FileAttributes.Normal, size, hash);


        public static FileMetadata MakeNewDirectoryMetadata(string name) => new FileMetadata(name, new DateTime(),
                                new DateTime(), new DateTime(), FileAttributes.Directory, 0, null);

        public Task<bool> IndexFileExistsAsync(string bsname, IndexFileType fileType)
        {
            return Task.Run(() => FileExists(GetIndexFilePath(bsname, fileType)));
        }

        public Task<byte[]> LoadIndexFileAsync(string bsname, IndexFileType fileType)
        {
            return Task.Run(() => ReadAllFileBytes(GetIndexFilePath(bsname, fileType)));
        }

        public void StoreIndexFileAsync(string bsname, IndexFileType fileType, byte[] data)
        {
            OverwriteOrCreateFile(GetIndexFilePath(bsname, fileType), data);
        }

        public Task<byte[]> LoadBlobAsync(byte[] hash)
        {
            return Task.Run(() => ReadAllFileBytes(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash))));
        }

        public Task<string> StoreBlobAsync(byte[] hash, byte[] data)
        {
            string relpath = GetBlobRelativePath(hash);
            OverwriteOrCreateFile(Path.Combine(BlobSaveDirectory, relpath), data);
            return Task.Run(() => relpath);
        }

        public void DeleteBlobAsync(byte[] hash, string fileId)
        {
            Task.Run(() => DeleteFile(Path.Combine(BlobSaveDirectory, GetBlobRelativePath(hash))));
        }

        private string GetIndexFilePath(string bsname, IndexFileType fileType)
        {
            string path;
            switch (fileType)
            {
                case IndexFileType.BlobIndex:
                    path = Path.Combine(DstRoot, "index", Core.BackupBlobIndexFile);
                    break;
                case IndexFileType.BackupSet:
                    path = Path.Combine(DstRoot, "index", "backupstores", bsname);
                    break;
                case IndexFileType.SettingsFile:
                    path = path = Path.Combine(DstRoot, Core.SettingsFilename);
                    break;
                default:
                    throw new ArgumentException("Unknown IndexFileType");
            }
            return path;
        }

        private string GetBlobRelativePath(byte[] hash)
        {
            // Save files with names given by their hashes
            // In order to keep the number of files per directory managable,
            // the first two bytes of the hash are stripped and used as 
            // the names of two nested directories into which the file
            // is placed.
            // Ex. hash = 3bc6e94a89 => relpath = 3b/c6/e94a89
            string hashstring = HashTools.ByteArrayToHexViaLookup32(hash);
            string dir1 = hashstring.Substring(0, 2);
            string dir2 = hashstring.Substring(2, 2);
            string fname = hashstring.Substring(4);

            return Path.Combine(dir1, dir2, fname);
        }
    }
}
