using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    /// <summary>
    /// Represents a directory.
    /// Includes the directory's metadata and name based access to its children.
    /// </summary>
    public class MetadataNode : ICustomSerializable<MetadataNode>
    {
        // Name of root node should be "."
        // A Directory is a special type of file and has its own metadata

        /// <summary>
        /// Metadata about this directory itself.
        /// </summary>
        public FileMetadata DirMetadata { get; set; }

        public Dictionary<string, MetadataNode> Directories { get; set; }
        public Dictionary<string, FileMetadata> Files { get; set; }

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
            if (directories != null)
            {
                Directories = directories;
            }
            else
            {
                Directories = new Dictionary<string, MetadataNode>();
            }
            if (files != null)
            {
                Files = files;
            }
            else
            {
                files = new Dictionary<string, FileMetadata>();
            }
        }

        public MetadataNode GetDirectory(string name)
        {
            if (Directories != null && Directories.ContainsKey(name))
            {
                return Directories[name];
            }
            return null;
        }

        public FileMetadata GetFile(string name)
        {
            if (Files != null && Files.ContainsKey(name))
            {
                return Files[name];
            }
            return null;
        }

        /// <summary>
        /// Adds or updates a directory.
        /// </summary>
        /// <param name="metadata"></param>
        public void AddDirectory(FileMetadata metadata)
        {
            if (Directories.ContainsKey(metadata.FileName))
            {
                Directories[metadata.FileName].DirMetadata = metadata;
            }
            else
            {
                MetadataNode ndir = new MetadataNode(metadata);
                Directories[metadata.FileName] = ndir;
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

        /// <summary>
        /// Serializes a node.
        /// Will recursively serialize all child nodes.
        /// Obviously breaks with circular references, but these should only occur
        /// with hard- (and soft-?) -links TODO: handle links
        /// </summary>
        /// <returns></returns>
        public byte[] serialize()
        {
            Dictionary<string, byte[]> mtdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // DirMetadata = FileMetadata DirMetadata.serialize()
            // Directories = enum_encode([Directories.Values MetadataNode.serialize(),... ])
            // Files = enum_encode([Files.Values FileMetadata.serialize(),... ])

            mtdata.Add("DirMetadata-v1", DirMetadata.serialize());
            mtdata.Add("Directories-v1", BinaryEncoding.enum_encode(from mn in Directories.Values.AsEnumerable() select mn.serialize()));
            mtdata.Add("Files-v1", BinaryEncoding.enum_encode(from fm in Files.Values.AsEnumerable() select fm.serialize()));

            return BinaryEncoding.dict_encode(mtdata);
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
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            FileMetadata dirmetadata = FileMetadata.deserialize(savedobjects["DirMetadata-v1"]);
            Dictionary<string, MetadataNode> directories = new Dictionary<string, MetadataNode>();
            foreach (var binmn in BinaryEncoding.enum_decode(savedobjects["Directories-v1"]))
            {
                MetadataNode newmn = MetadataNode.deserialize(binmn);
                directories.Add(newmn.DirMetadata.FileName, newmn);
            }
            Dictionary<string, FileMetadata> files = new Dictionary<string, FileMetadata>();
            foreach (var binfm in BinaryEncoding.enum_decode(savedobjects["Files-v1"]))
            {
                FileMetadata newfm = FileMetadata.deserialize(binfm);
                files.Add(newfm.FileName, newfm);
            }

            return new MetadataNode(dirmetadata, directories, files);
        }
    }
}
