using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BackupCore
{
    public class MetadataTree : ICustomSerializable<MetadataTree>
    {
        public MetadataNode Root { get; set; }

        public MetadataTree(FileMetadata rootmetadata)
        {
            Root = new MetadataNode(rootmetadata, null);
        }

        private MetadataTree(MetadataNode root)
        {
            Root = root;
        }

        public bool HasDirectory(string relpath)
        {
            return GetDirectory(relpath) != null;
        }

        public FileMetadata GetDirectoryMetadata(string relpath)
        {
            return GetDirectory(relpath).DirMetadata;
        }

        public FileMetadata GetFile(string relpath)
        {
            int slash = relpath.LastIndexOf(Path.DirectorySeparatorChar);
            if (slash == -1) // file must exist in "root" assume '\\' before path
            {
                relpath = Path.DirectorySeparatorChar + relpath;
                slash = 0;
            }
            MetadataNode parent = GetDirectory(relpath.Substring(0, slash));
            if (parent != null)
            {
                return parent.GetFile(relpath.Substring(slash + 1, relpath.Length - slash - 1));
            }
            return null;
        }
        
        public MetadataNode GetDirectory(string relpath)
        {
            if (relpath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            return GetDirectory(relpath, Root);
        }

        /// <summary>
        /// Recursively locates the directory specified by path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="current_dir"></param>
        /// <returns></returns>
        private MetadataNode GetDirectory(string path, MetadataNode current_dir)
        {
            if (path.StartsWith(Path.DirectorySeparatorChar.ToString()))
            {
                path = path.Substring(1);
            }
            if (path == "")
            {
                return current_dir;
            }
            int slash = path.IndexOf(Path.DirectorySeparatorChar);
            if (slash != -1)
            {
                string nextdirname = path.Substring(0, slash);
                string nextpath = path.Substring(slash + 1, path.Length - slash - 1);
                MetadataNode nextdir = current_dir.GetDirectory(nextdirname);
                if (nextdir == null)
                {
                    return null;
                }
                return GetDirectory(nextpath, nextdir);
            }
            else
            {
                return current_dir.GetDirectory(path);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="dirpath">Relative path NOT containing name of new directory.</param>
        /// <param name="metadata"></param>
        public void AddDirectory(string dirpath, FileMetadata metadata)
        {
            GetDirectory(dirpath).AddDirectory(metadata);
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

        public IEnumerable<byte[]> GetAllFileHashes()
        {
            return Root.GetAllFileHashes();
        }

        /// <summary>
        /// Serializes the metadata store.
        /// As of now the "store" is just the root node and its descendents.
        /// So we just return the serialized root which contains all of its children.
        /// </summary>
        /// <returns></returns>
        public byte[] serialize()
        {
            return Root.serialize();
        }

        public static MetadataTree deserialize(byte[] data)
        {
            return new MetadataTree(MetadataNode.deserialize(data));
        }
    }
}
