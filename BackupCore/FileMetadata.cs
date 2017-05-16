using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;

namespace BackupCore
{
    /// <summary>
    /// Stores basic file metadata + list of hashes of file blocks
    /// </summary>
    public class FileMetadata : ICustomSerializable<FileMetadata>
    {
        public string FileName { get; set; }
        
        public DateTime DateAccessedUTC { get; set; }
        
        private long NumDateAccessedUTC
        {
            get { return DateAccessedUTC.Ticks; }
            set { DateAccessedUTC = new DateTime(value); }
        }
        
        public DateTime DateModifiedUTC { get; set; }
        
        private long NumDateModifiedUTC
        {
            get { return DateModifiedUTC.Ticks; }
            set { DateModifiedUTC = new DateTime(value); }
        }
        
        public DateTime DateCreatedUTC { get; set; }
        
        private long NumDateCreatedUTC
        {
            get { return DateCreatedUTC.Ticks; }
            set { DateCreatedUTC = new DateTime(value); }
        }

        public FileAttributes Attributes { get; set; }
        
        public long FileSize { get; set; }
        
        public byte[] FileHash { get; set; }

        /// <summary>
        /// New FileMetadata by explicitly specifying each field of
        /// a file's metadata.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="dateaccessed"></param>
        /// <param name="datemodified"></param>
        /// <param name="datecreated"></param>
        /// <param name="filesize"></param>
        public FileMetadata(string filename, DateTime dateaccessed, DateTime datemodified, 
            DateTime datecreated, FileAttributes attributes, long filesize, byte[] filehash)
        {
            FileName = filename;
            DateAccessedUTC = dateaccessed;
            DateModifiedUTC = datemodified;
            Attributes = attributes;
            FileSize = filesize;
            DateCreatedUTC = datecreated;
            FileHash = filehash;
        }

        /// <summary>
        /// New FileMetadata by examining a file on disk.
        /// </summary>
        /// <param name="filepath"></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="System.Security.SecurityException"/>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="UnauthorizedAccessException"/>
        /// <exception cref="PathTooLongException"/>
        /// <exception cref="NotSupportedException"/>
        public FileMetadata(string filepath)
        {
            FileName = Path.GetFileName(filepath);
            FileInfo fi = new FileInfo(filepath);
            DateAccessedUTC = fi.LastAccessTimeUtc;
            DateModifiedUTC = fi.LastWriteTimeUtc;
            Attributes = fi.Attributes;
            if ((Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                FileSize = 0;
            }
            else
            {
                FileSize = fi.Length;
            }
            DateCreatedUTC = fi.CreationTimeUtc;
        }

        public void WriteOutMetadata(string filepath)
        {
            FileSystemInfo fi = new FileInfo(filepath);
            if ((Attributes & FileAttributes.Directory) == FileAttributes.Directory)
            {
                // For some reason cannot assign back to a FileInfo of a directory
                fi = new DirectoryInfo(filepath);
            }
            fi.LastAccessTimeUtc = DateAccessedUTC;
            fi.LastWriteTimeUtc = DateModifiedUTC;
            fi.CreationTimeUtc = DateCreatedUTC;
            if (fi.Attributes != 0)
            {
                fi.Attributes = Attributes;
            }
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> fmdata = new Dictionary<string, byte[]>();
            // v1
            // -"-v1" (suffix for below)
            // FileName = ASCII encoded
            // DateAccessedUTC = BitConverter.GetBytes(long NumDateAccessedUTC)
            // DateModifiedUTC = BitConverter.GetBytes(long NumDateModifiedUTC)
            // DateCreatedUTC = BitConverter.GetBytes(long NumDateCreatedUTC)
            // FileSize = BitConverter.GetBytes(long)
            // BlocksHashes = enum_encode(BlocksHashes) or byte[0] if BlocksHashes==null

            // -v2
            // all v1 data (suffix unchanged) +
            // Attributes = BitConverter.GetBytes((int)Attributes)

            // -v3
            // Breaks compatibility with v1&v2
            // all v2 data +
            // FileHash = FileHash or byte[0] if FileHash==null

            fmdata.Add("FileName-v1", Encoding.ASCII.GetBytes(FileName));
            fmdata.Add("DateAccessedUTC-v1", BitConverter.GetBytes(NumDateAccessedUTC));
            fmdata.Add("DateModifiedUTC-v1", BitConverter.GetBytes(NumDateModifiedUTC));
            fmdata.Add("DateCreatedUTC-v1", BitConverter.GetBytes(NumDateCreatedUTC));
            fmdata.Add("FileSize-v1", BitConverter.GetBytes(FileSize));
            fmdata.Add("Attributes-v2", BitConverter.GetBytes((int)Attributes));
            if (FileHash != null)
            {
                fmdata.Add("FileHash-v3", FileHash);
            }
            else
            {
                fmdata.Add("FileHash-v3", new byte[0]);
            }

            return BinaryEncoding.dict_encode(fmdata);
        }

        public static FileMetadata deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            string filename = Encoding.ASCII.GetString(savedobjects["FileName-v1"]);
            long numdateaccessed = BitConverter.ToInt64(savedobjects["DateAccessedUTC-v1"], 0);
            long numdatemodified = BitConverter.ToInt64(savedobjects["DateModifiedUTC-v1"], 0);
            long numdatecreated = BitConverter.ToInt64(savedobjects["DateCreatedUTC-v1"], 0);
            long filesize = BitConverter.ToInt64(savedobjects["FileSize-v1"], 0);

            FileAttributes attributes = 0;
            if (savedobjects.ContainsKey("Attributes-v2"))
            {
                attributes = (FileAttributes)BitConverter.ToInt32(savedobjects["Attributes-v2"], 0);
            }
            
            byte[] filehash = savedobjects["FileHash-v3"];

            return new FileMetadata(filename,
                new DateTime(numdateaccessed),
                new DateTime(numdatemodified),
                new DateTime(numdatecreated),
                attributes,
                filesize,
                filehash);
        }
    }
}
