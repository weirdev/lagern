using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BackupCore
{
    class MetadataStore : ICustomSerializable<MetadataStore>
    {
        MetadataNode Root { get; set; }

        MetadataStore(FileMetadata rootinfo)
        {
            Root = new MetadataNode(rootinfo);
        }

        private MetadataStore(MetadataNode root)
        {
            Root = root;
        }

        public FileMetadata GetDirectoryMetadata(string relpath)
        {
            return GetDirectory(relpath).DirMetadata;
        }
        
        // TODO: handle ".." in paths
        private MetadataNode GetDirectory(string relpath)
        {
            if (relpath.StartsWith("/"))
            {
                throw new ArgumentException("Paths must be relative and cannot begin with \"/\".");
            }
            if (relpath.EndsWith("/"))
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
            int slash = path.IndexOf('/');
            if (slash != -1)
            {
                string nextdirname = path.Substring(0, slash);
                string nextpath = path.Substring(slash + 1, path.Length - slash - 1);
                MetadataNode nextdir;
                if (nextdirname != ".")
                {
                    nextdir = current_dir.GetDirectory(nextdirname);
                }
                else
                {
                    nextdir = current_dir;
                }
                return GetDirectory(nextpath, nextdir);
            }
            else
            {
                if (path != ".")
                {
                    return current_dir.GetDirectory(path);
                }
                else
                {
                    return current_dir;
                }
            }
        }

        public FileMetadata GetFile(string relpath)
        {
            if (relpath.StartsWith("/"))
            {
                throw new ArgumentException("Paths must be relative and cannot begin with \"/\".");
            }
            if (relpath.EndsWith("/"))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            int slash = relpath.LastIndexOf('/');
            if (slash != -1)
            {
                MetadataNode parent = GetDirectory(relpath.Substring(0, slash));
                return parent.GetFile(relpath.Substring(slash + 1, relpath.Length - slash - 1));
            }
            else // only filename given, must exist in "root"
            {
                return Root.GetFile(relpath);
            }
        }

        public void AddDirectory(string relpath, FileMetadata metadata)
        {
            if (relpath.EndsWith("/"))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            int lastslash = relpath.LastIndexOf('/');
            if (lastslash != -1)
            {
                string dirpath = relpath.Substring(0, lastslash);
                MetadataNode parent = GetDirectory(dirpath);
                parent.AddDirectory(metadata);
            }
            else // only directory name given, must exist in "root"
            {
                Root.AddDirectory(metadata);
            }
        }

        /// <summary>
        /// Adds a file to its parent folder.
        /// </summary>
        /// <param name="relpath">Relative file path containing filename.</param>
        /// <param name="metadata"></param>
        public void AddFile(string relpath, FileMetadata metadata)
        {
            if (relpath.EndsWith("/"))
            {
                relpath = relpath.Substring(0, relpath.Length - 1);
            }
            int lastslash = relpath.LastIndexOf('/');
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

        public MetadataStore deserialize(byte[] data)
        {
            return new MetadataStore(MetadataNode.deserialize(data));
        }

        /// <summary>
        /// Represents a directory.
        /// Includes the directory's metadata and name based access to its children.
        /// </summary>
        protected class MetadataNode : ICustomSerializable<MetadataNode>
        {
            // Name of root node should be "."
            // A Directory is a special type of file and has its own metadata

            /// <summary>
            /// Metadata about this directory itself.
            /// </summary>
            public FileMetadata DirMetadata { get; set; }

            private Dictionary<string, MetadataNode> Directories;
            private Dictionary<string, FileMetadata> Files;
            
            public MetadataNode(FileMetadata metadata)
            {
                DirMetadata = metadata;
                Directories = new Dictionary<string, MetadataNode>();
                Files = new Dictionary<string, FileMetadata>();
            }

            private MetadataNode(FileMetadata metadata, Dictionary<string, MetadataNode> directories,
                Dictionary<string, FileMetadata> files)
            {
                DirMetadata = metadata;

            }

            public MetadataNode GetDirectory(string name)
            {
                if (Directories.ContainsKey(name))
                {
                    return Directories[name];
                }
                return null;
            }

            public FileMetadata GetFile(string name)
            {
                if (Files.ContainsKey(name))
                {
                    return Files[name];
                }
                return null;
            }

            public void AddDirectory(FileMetadata metadata)
            {
                MetadataNode ndir = new MetadataNode(metadata);
                Directories.Add(metadata.FileName, ndir);
            }

            public void AddFile(FileMetadata metadata)
            {
                Files.Add(metadata.FileName, metadata);
            }

            /// <summary>
            /// Serializes a node.
            /// Will recursively serialize all child nodes.
            /// Obviously breaks with circular references, but these should only occur
            /// with hard- (and soft-?) -links TODO: handle links
            /// </summary>
            /// <returns></returns>
            public byte[] serialize()
            {
                byte[] dirmetadatabytes = DirMetadata.serialize();

                List<byte> dirsbinrep = new List<byte>();
                foreach (var mnode in Directories.Values)
                {
                    BinaryEncoding.encode(mnode.serialize(), dirsbinrep);
                }
                byte[] directoriesbytes = dirsbinrep.ToArray();

                List<byte> filesbinrep = new List<byte>();
                foreach (var filem in Files.Values)
                {
                    BinaryEncoding.encode(filem.serialize(), filesbinrep);
                }
                byte[] filesbytes = filesbinrep.ToArray();

                List<byte> allbinrep = new List<byte>();
                BinaryEncoding.encode(dirmetadatabytes, allbinrep);
                BinaryEncoding.encode(directoriesbytes, allbinrep);
                BinaryEncoding.encode(filesbytes, allbinrep);

                return allbinrep.ToArray();
            }

            /// <summary>
            /// Deserializes a node.
            /// Will recursively deserialize all child nodes.
            /// Obviously breaks with circular references, but these should only occur
            /// with hard- (and soft-?) -links
            /// </summary>
            /// <param name="data"></param>
            /// <returns></returns>
            public static MetadataNode deserialize(byte[] data)
            {
                // TEST: This when directory lacks child directories and / or files
                byte[] dirmetadatabytes;
                byte[] directoriesbytes;
                byte[] filesbytes;

                List<byte[]> savedobjects = BinaryEncoding.decode(data);
                dirmetadatabytes = savedobjects[0];

                directoriesbytes = savedobjects[1];
                filesbytes = savedobjects[2];

                Dictionary<string, MetadataNode> directories = new Dictionary<string, MetadataNode>();
                foreach (var dir in BinaryEncoding.decode(directoriesbytes))
                {
                    MetadataNode mnode = MetadataNode.deserialize(dir);
                    directories.Add(mnode.DirMetadata.FileName, mnode);
                }

                Dictionary<string, FileMetadata> files = new Dictionary<string, FileMetadata>();
                foreach (var file in BinaryEncoding.decode(filesbytes))
                {
                    FileMetadata filem = FileMetadata.deserialize(file);
                    files.Add(filem.FileName, filem);
                }

                return new MetadataNode(FileMetadata.deserialize(dirmetadatabytes),
                    directories, files);
            }
        }
    }
}
