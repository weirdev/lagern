using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Collections;
using BackupCore.Models;

namespace BackupCore
{
    /// <summary>
    /// Represents a directory.
    /// Includes the directory's metadata and name based access to its children.
    /// </summary>
    public class MetadataNode
    {
        // Name of root node should be "."
        // A Directory is a special type of file and has its own metadata

        public MetadataNode? Parent { get; set; }

        /// <summary>
        /// Metadata about this directory itself.
        /// </summary>
        public FileMetadata DirMetadata { get; set; }

        public ConcurrentDictionary<string, MetadataNode> Directories { get; set; }
        public ConcurrentDictionary<string, FileMetadata> Files { get; private set; }

        public string Path
        {
            get
            {
                if (Parent != null)
                {
                    return System.IO.Path.Combine(Parent.Path, DirMetadata.FileName);
                }
                else
                {
                    return System.IO.Path.DirectorySeparatorChar.ToString();
                }
            }
        }

        public MetadataNode(FileMetadata metadata, MetadataNode? parent)
        {
            Parent = parent;
            DirMetadata = metadata;
            Directories = new ConcurrentDictionary<string, MetadataNode>();
            Files = new ConcurrentDictionary<string, FileMetadata>();
        }


        // For serialization/deserialization
        #pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        private MetadataNode() { }
        #pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.

        public bool HasDirectory(string relpath)
        {
            return GetDirectory(relpath) != null;
        }

        public FileMetadata GetDirectoryMetadata(string relpath)
        {
            MetadataNode? dir = GetDirectory(relpath);
            if (dir != null)
            {
                return dir.DirMetadata;
            }
            throw new Exception("No directory exists at that path");
        }

        /// <summary>
        /// Gets a directory relative to this directory, returns null if does not exist.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns></returns>
        public MetadataNode? GetDirectory(string relpath)
        {
            if (relpath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            if (relpath.StartsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(1);
            }
            if (relpath == "")
            {
                return this;
            }
            if (relpath == ".")
            {
                return this;
            }
            if (relpath == "..")
            {
                return Parent;
            }
            int slash = relpath.IndexOf(System.IO.Path.DirectorySeparatorChar);
            if (slash != -1)
            {
                string nextdirname = relpath.Substring(0, slash);
                string nextpath = relpath.Substring(slash + 1, relpath.Length - slash - 1);
                MetadataNode? nextdir = GetDirectory(nextdirname);
                if (nextpath == "")
                {
                    return nextdir;
                }
                if (nextdir == null)
                {
                    return null;
                }
                return nextdir.GetDirectory(nextpath);
            }
            else
            {
                if (Directories != null && Directories.ContainsKey(relpath))
                {
                    return Directories[relpath];
                }
            }
            return null;
        }

        public FileMetadata? GetFile(string relpath)
        {
            int slash = relpath.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
            if (slash == -1) // file must exist in "root" assume '\\' before path
            {
                relpath = System.IO.Path.DirectorySeparatorChar + relpath;
                slash = 0;
            }
            MetadataNode? parent = GetDirectory(relpath.Substring(0, slash));
            if (parent != null)
            {
                string name = relpath.Substring(slash + 1, relpath.Length - slash - 1);
                if (parent.Files != null && parent.Files.ContainsKey(name))
                {
                    return parent.Files[name];
                }
            }
            return null;
        }

        public void AddDirectory(string dirpath, FileMetadata metadata)
        {
            MetadataNode? parentDir = GetDirectory(dirpath);
            if (parentDir != null)
            {
                parentDir.AddDirectory(metadata);
            }
            else
            {
                throw new Exception("The specified parent directory does not exist");
            }
        }

        /// <summary>
        /// Adds or updates a directory.
        /// </summary>
        /// <param name="metadata"></param>
        public MetadataNode AddDirectory(FileMetadata metadata)
        {
            return Directories.AddOrUpdate(metadata.FileName, 
                (_) => new MetadataNode(metadata, this), 
                (_, existingNode) =>
                    {
                        existingNode.DirMetadata = metadata;
                        return existingNode;
                    });
        }

        /// <summary>
        /// Adds a file to its parent folder.
        /// </summary>
        /// <param name="dirpath">Relative path NOT containing filename.</param>
        /// <param name="metadata"></param>
        public void AddFile(string dirpath, FileMetadata metadata)
        {
            MetadataNode? parentDir = GetDirectory(dirpath);
            if (parentDir != null)
            {
                parentDir.AddFile(metadata);
            }
            else
            {
                throw new Exception("The specified parent directory does not exist");
            }
        }

        /// <summary>
        /// Adds or updates a file.
        /// </summary>
        /// <param name="metadata"></param>
        public void AddFile(FileMetadata metadata)
        {
            Files[metadata.FileName] = metadata;
        }
        
        public IEnumerable<byte[]> GetAllFileHashes()
        {
            foreach (var f in Files)
            {
                if (f.Value.FileHash == null)
                {
                    throw new NullReferenceException("Stored file hashes cannot be null");
                }
                yield return f.Value.FileHash;
            }
            foreach (var d in Directories)
            {
                foreach (var f in d.Value.GetAllFileHashes())
                {
                    yield return f;
                }
            }
        }

        /// <summary>
        /// Non recursive equality check
        /// </summary>
        public bool NodeEquals(MetadataNode node)
        {
            return EqualityComparer<FileMetadata>.Default.Equals(DirMetadata, node.DirMetadata) &&
                   Directories.Keys.ToHashSet().SetEquals(node.Directories.Keys) &&
                   Files.Select(kv => (kv.Key, kv.Value)).ToHashSet().SetEquals(node.Files.Select(kv => (kv.Key, kv.Value))) &&
                   Path == node.Path;
        }

        /// <summary>
        /// Store this node and its descendents in a blobstore
        /// Breaks with circular references, but these should only occur
        /// with hard- (and soft-?) -links TODO: handle links
        /// </summary>
        /// <param name="blobs"></param>
        /// <param name="storeGetHash">Function called to store node data, returns hash</param>
        /// <returns>The hash of the stored tree and a tree of its child hashes.</returns>
        public (byte[] nodehash, HashTreeNode node) Store(Func<byte[], byte[]> storeGetHash)
        {
            List<(byte[] nodehash, HashTreeNode? node)> children = new List<(byte[] nodehash, HashTreeNode? node)>();
            List<byte[]> dirhashes = new List<byte[]>();
            foreach (MetadataNode dir in Directories.Values)
            {
                var (newhash, newnode) = dir.Store(storeGetHash);
                dirhashes.Add(newhash);
                children.Add((newhash, newnode));
            }
            foreach (var fm in Files.Values.AsEnumerable())
            {
                if (fm.FileHash == null)
                {
                    throw new NullReferenceException("Stored filehashes cannot be null");
                }
                children.Add((fm.FileHash, null));
            }
            Dictionary<string, byte[]> mtdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // DirMetadata = FileMetadata DirMetadata.serialize()
            // Directories = enum_encode([Directories.Values MetadataNode.serialize(),... ])
            // Files = enum_encode([Files.Values FileMetadata.serialize(),... ])
            // -"-v2"
            // Directories = enum_encode([dirrefs,...])
            // "-v3"
            // DirectoriesMultiblock = enum_encode([BitConverter.GetBytes(multiblock),...])
            // -v4
            // removed DirectoriesMultiblock
            mtdata.Add("DirMetadata-v1", DirMetadata.Serialize());
            mtdata.Add("Files-v1", BinaryEncoding.enum_encode(Files.Values.AsEnumerable()
                                                              .Select(fm => fm.Serialize())));

            mtdata.Add("Directories-v2", BinaryEncoding.enum_encode(dirhashes));
            
            byte[] nodehash = storeGetHash(BinaryEncoding.dict_encode(mtdata));
            HashTreeNode node = new HashTreeNode(children);
            return (nodehash, node);
        }

        public static MetadataNode Load(BlobStore blobs, byte[] hash, MetadataNode? parent = null)
        {
            var curmn = new MetadataNode();

            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(blobs.RetrieveData(hash));
            FileMetadata dirmetadata = FileMetadata.Deserialize(savedobjects["DirMetadata-v1"]);
            curmn.DirMetadata = dirmetadata;
            ConcurrentDictionary<string, FileMetadata> files = new ConcurrentDictionary<string, FileMetadata>();
            var encodedFiles = BinaryEncoding.enum_decode(savedobjects["Files-v1"]) ?? new List<byte[]?>();
            foreach (var binfm in encodedFiles)
            {
                if (binfm == null)
                {
                    throw new NullReferenceException("Encoded file metadatas cannot be null");
                }
                FileMetadata newfm = FileMetadata.Deserialize(binfm);
                files[newfm.FileName] = newfm;
            }
            curmn.Files = files;
            ConcurrentDictionary<string, MetadataNode> directories = new ConcurrentDictionary<string, MetadataNode>();
            var dirs = BinaryEncoding.enum_decode(savedobjects["Directories-v2"]) ?? new List<byte[]?>();
            for (int i = 0; i < dirs.Count; i++)
            {
                var dir = dirs[i] ?? throw new NullReferenceException("Encoded directory cannot be null");
                MetadataNode newmn = Load(blobs, dir, curmn);
                directories[newmn.DirMetadata.FileName] = newmn;
            }
            curmn.Parent = parent;
            curmn.Directories = directories;
            return curmn;
        }

        /// <summary>
        /// Gets the reference (hash) for all immediate data of the metadatanode without loading
        /// the node into memory. Useful to keep memory usage low.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static IEnumerable<byte[]> GetAllReferencesWithoutLoad(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            var directories = BinaryEncoding.enum_decode(savedobjects["Directories-v2"]) ?? new List<byte[]?>();
            foreach (var reference in directories)
            {
                if (reference == null)
                {
                    throw new NullReferenceException("Directory references cannot be null");
                }
                yield return reference;
            }
            var files = BinaryEncoding.enum_decode(savedobjects["Files-v1"]) ?? new List<byte[]?>();
            foreach (byte[]? filedata in files)
            {
                if (filedata == null)
                {
                    throw new NullReferenceException("Encoded file data cannot be null");
                }
                FileMetadata fm = FileMetadata.Deserialize(filedata);
                if (fm.FileHash == null)
                {
                    throw new NullReferenceException("Stored file hashes cannot be null");
                }
                yield return fm.FileHash;
            }
        }

        public static IEnumerable<byte[]> GetImmediateChildNodeReferencesWithoutLoad(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            var dirs = BinaryEncoding.enum_decode(savedobjects["Directories-v2"]) ?? new List<byte[]?>();
            for (int i = 0; i < dirs.Count; i++)
            {
                var dir = dirs[i] ?? throw new NullReferenceException("Directory reference cannot be null");
                yield return dir;
            }
        }

        public static IEnumerable<byte[]> GetImmediateFileReferencesWithoutLoad(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            var encodedFiles = BinaryEncoding.enum_decode(savedobjects["Files-v1"]) ?? new List<byte[]?>();
            foreach (var filemdata in encodedFiles)
            {
                if (filemdata == null)
                {
                    throw new Exception("File metadata objects cannot be null here");
                }
                FileMetadata fm = FileMetadata.Deserialize(filemdata);
                if (fm.FileHash == null)
                {
                    throw new NullReferenceException("Stored files must have file hashes");
                }
                yield return fm.FileHash;
            }
        }
    }
}
