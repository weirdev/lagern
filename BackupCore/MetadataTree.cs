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
        MetadataNode Root { get; set; }

        public MetadataTree() { }

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
                relpath = '\\' + relpath;
            }
            MetadataNode parent = GetDirectory(relpath.Substring(0, slash));
            return parent.GetFile(relpath.Substring(slash + 1, relpath.Length - slash - 1));
        }
        
        public MetadataNode GetDirectory(string relpath)
        {
            if (relpath.StartsWith(Path.DirectorySeparatorChar.ToString())) // always start at root of tree, toss first slash
            {
                relpath = relpath.Substring(1);
            }
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
            if (path.StartsWith("\\"))
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

        public void AddDirectory(string relpath, FileMetadata metadata)
        {
            if (relpath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            int lastslash = relpath.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastslash != -1)
            {
                string dirpath = relpath.Substring(0, lastslash);
                MetadataNode parent = GetDirectory(dirpath);
                parent.AddDirectory(metadata);
            }
            else // only directory name given, must exist in "root"
            {
                if (relpath == ".")
                {
                    if (Root != null)
                    {
                        Root.DirMetadata = metadata;
                    }
                    else
                    {
                        Root = new MetadataNode(metadata, null);
                    }
                }
                else
                {
                    Root.AddDirectory(metadata);
                }
            }
        }

        /// <summary>
        /// Adds a file to its parent folder.
        /// </summary>
        /// <param name="relpath">Relative file path containing filename.</param>
        /// <param name="metadata"></param>
        public void AddFile(string relpath, FileMetadata metadata)
        {
            if (relpath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            int lastslash = relpath.LastIndexOf(Path.DirectorySeparatorChar);
            if (lastslash != -1)
            {
                string dirpath = relpath.Substring(0, lastslash);
                MetadataNode parent = GetDirectory(dirpath);
                parent.AddFile(metadata);
            }
            else // only filename given, must exist in "root"
            {
                Root.AddFile(metadata);
            }
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
