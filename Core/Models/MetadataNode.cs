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

        public MetadataNode Parent { get; set; }

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

        public MetadataNode(FileMetadata metadata, MetadataNode parent)
        {
            Parent = parent;
            DirMetadata = metadata;
            Directories = new ConcurrentDictionary<string, MetadataNode>();
            Files = new ConcurrentDictionary<string, FileMetadata>();
        }

        // For serialization/deserialization
        private MetadataNode() { }

        public bool HasDirectory(string relpath)
        {
            return GetDirectory(relpath) != null;
        }

        public FileMetadata GetDirectoryMetadata(string relpath)
        {
            return GetDirectory(relpath).DirMetadata;
        }

        /// <summary>
        /// Gets a directory relative to this directory, returns null if does not exist.
        /// </summary>
        /// <param name="relpath"></param>
        /// <returns></returns>
        public MetadataNode GetDirectory(string relpath)
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
                MetadataNode nextdir = GetDirectory(nextdirname);
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

        public FileMetadata GetFile(string relpath)
        {
            int slash = relpath.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
            if (slash == -1) // file must exist in "root" assume '\\' before path
            {
                relpath = System.IO.Path.DirectorySeparatorChar + relpath;
                slash = 0;
            }
            MetadataNode parent = GetDirectory(relpath.Substring(0, slash));
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
            GetDirectory(dirpath).AddDirectory(metadata);
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
            GetDirectory(dirpath).AddFile(metadata);
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
        /// Store this node and its descendents in a blobstore
        /// Breaks with circular references, but these should only occur
        /// with hard- (and soft-?) -links TODO: handle links
        /// </summary>
        /// <param name="blobs"></param>
        /// <param name="storeGetHash">Function called to store node data, returns hash</param>
        /// <returns>The hash of the stored tree and a tree of its child hashes.</returns>
        public (byte[] nodehash, HashTreeNode node) Store(Func<byte[], byte[]> storeGetHash)
        {
            List<(byte[] nodehash, HashTreeNode node)> children = new List<(byte[] nodehash, HashTreeNode node)>();
            List<byte[]> dirhashes = new List<byte[]>();
            foreach (MetadataNode dir in Directories.Values)
            {
                var (newhash, newnode) = dir.Store(storeGetHash);
                dirhashes.Add(newhash);
                children.Add((newhash, newnode));
            }
            foreach (var fm in Files.Values.AsEnumerable())
            {
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
            mtdata.Add("DirMetadata-v1", DirMetadata.serialize());
            mtdata.Add("Files-v1", BinaryEncoding.enum_encode(Files.Values.AsEnumerable()
                                                              .Select(fm => fm.serialize())));

            mtdata.Add("Directories-v2", BinaryEncoding.enum_encode(dirhashes));
            
            byte[] nodehash = storeGetHash(BinaryEncoding.dict_encode(mtdata));
            HashTreeNode node = new HashTreeNode(children);
            return (nodehash, node);
        }

        public static MetadataNode Load(BlobStore blobs, byte[] hash, MetadataNode parent = null)
        {
            var curmn = new MetadataNode();

            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(blobs.RetrieveData(hash));
            FileMetadata dirmetadata = FileMetadata.deserialize(savedobjects["DirMetadata-v1"]);
            curmn.DirMetadata = dirmetadata;
            ConcurrentDictionary<string, FileMetadata> files = new ConcurrentDictionary<string, FileMetadata>();
            foreach (var binfm in BinaryEncoding.enum_decode(savedobjects["Files-v1"]))
            {
                FileMetadata newfm = FileMetadata.deserialize(binfm);
                files[newfm.FileName] = newfm;
            }
            curmn.Files = files;
            ConcurrentDictionary<string, MetadataNode> directories = new ConcurrentDictionary<string, MetadataNode>();
            var dirs = BinaryEncoding.enum_decode(savedobjects["Directories-v2"]);
            for (int i = 0; i < dirs.Count; i++)
            {
                MetadataNode newmn = Load(blobs, dirs[i], curmn);
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
            foreach (var reference in BinaryEncoding.enum_decode(savedobjects["Directories-v2"]))
            {
                yield return reference;
            }
            foreach (byte[] filedata in BinaryEncoding.enum_decode(savedobjects["Files-v1"]))
            {
                FileMetadata fm = FileMetadata.deserialize(filedata);
                yield return fm.FileHash;
            }
        }

        public static IEnumerable<byte[]> GetImmediateChildNodeReferencesWithoutLoad(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            var dirs = BinaryEncoding.enum_decode(savedobjects["Directories-v2"]);
            for (int i = 0; i < dirs.Count; i++)
            {
                yield return dirs[i];
            }
        }

        public static IEnumerable<byte[]> GetImmediateFileReferencesWithoutLoad(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            foreach (var filemdata in BinaryEncoding.enum_decode(savedobjects["Files-v1"]))
            {
                FileMetadata fm = FileMetadata.deserialize(filemdata);
                yield return fm.FileHash;
            }
        }
    }
}
