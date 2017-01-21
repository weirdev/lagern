using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using System.Security.AccessControl;

namespace BackupCore
{
    /// <summary>
    /// Stores basic file metadata + list of hashes of file blocks
    /// </summary>
    [DataContract]
    public class FileMetadata : ICustomSerializable<FileMetadata>
    {
        [DataMember]
        public string FileName { get; set; }

        [IgnoreDataMember]
        public DateTimeOffset DateAccessed { get; set; }

        [DataMember]
        private long NumDateAccessed
        {
            get { return DateAccessed.Ticks; }
            set { DateAccessed = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [IgnoreDataMember]
        public DateTimeOffset DateModified { get; set; }

        [DataMember]
        private long NumDateModified
        {
            get { return DateModified.Ticks; }
            set { DateModified = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [IgnoreDataMember]
        public DateTimeOffset DateCreated { get; set; }

        [DataMember]
        private long NumDateCreated
        {
            get { return DateCreated.Ticks; }
            set { DateCreated = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [DataMember]
        public long FileSize { get; set; }

        [DataMember]
        public List<byte[]> BlocksHashes { get; set; }

        /// <summary>
        /// New FileMetadata by explicitly specifying each field of
        /// a file's metadata.
        /// </summary>
        /// <param name="filename"></param>
        /// <param name="dateaccessed"></param>
        /// <param name="datemodified"></param>
        /// <param name="datecreated"></param>
        /// <param name="filesize"></param>
        public FileMetadata(string filename, DateTimeOffset dateaccessed, DateTimeOffset datemodified, 
            DateTimeOffset datecreated, long filesize, List<byte[]> blockshashes)
        {
            FileName = filename;
            DateAccessed = dateaccessed;
            DateModified = datemodified;
            FileSize = filesize;
            DateCreated = datecreated;
            BlocksHashes = blockshashes;
        }

        /// <summary>
        /// New FileMetadata by examining a file on disk.
        /// </summary>
        /// <param name="filepath"></param>
        public FileMetadata(string filepath)
        {
            FileName = Path.GetFileName(filepath);
            FileInfo fi = new FileInfo(filepath);
            DateAccessed = fi.LastAccessTimeUtc;
            DateModified = fi.LastWriteTimeUtc;
            if (fi.Attributes == FileAttributes.Directory)
            {
                FileSize = 0;
            }
            else
            {
                FileSize = fi.Length;
            }
            DateCreated = fi.CreationTimeUtc;
        }

        public void WriteOut(string filepath)
        {
            FileInfo fi = new FileInfo(filepath);
            fi.LastAccessTimeUtc = DateAccessed.DateTime;
            fi.LastWriteTimeUtc = DateModified.DateTime;
            fi.CreationTimeUtc = DateCreated.DateTime;
        }

        public byte[] serialize()
        {
            byte[] filenamebytes = Encoding.ASCII.GetBytes(FileName);
            byte[] dateaccessedbytes = BitConverter.GetBytes(NumDateAccessed);
            byte[] datemodifiedbytes = BitConverter.GetBytes(NumDateModified);
            byte[] datecreatedbytes = BitConverter.GetBytes(NumDateCreated);
            byte[] filesizebytes = BitConverter.GetBytes(FileSize);

            List<byte> binrep = new List<byte>();

            BinaryEncoding.encode(filenamebytes, binrep);
            BinaryEncoding.encode(dateaccessedbytes, binrep);
            BinaryEncoding.encode(datemodifiedbytes, binrep);
            BinaryEncoding.encode(datecreatedbytes, binrep);
            BinaryEncoding.encode(filesizebytes, binrep);
            if (BlocksHashes != null)
            {
                foreach (var hash in BlocksHashes)
                {
                    BinaryEncoding.encode(hash, binrep);
                }
            }

            return binrep.ToArray();
        }

        public static FileMetadata deserialize(byte[] data)
        {
            byte[] filenamebytes;
            byte[] dateaccessedbytes;
            byte[] datemodifiedbytes;
            byte[] datecreatedbytes;
            byte[] filesizebytes;

            List<byte[]> savedobjects = BinaryEncoding.decode(data);
            filenamebytes = savedobjects[0];
            dateaccessedbytes = savedobjects[1];
            datemodifiedbytes = savedobjects[2];
            datecreatedbytes = savedobjects[3];
            filesizebytes = savedobjects[4];

            List<byte[]> blockshashes = savedobjects.GetRange(5, savedobjects.Count - 5);

            long numdateaccessed = BitConverter.ToInt64(dateaccessedbytes, 0);
            long numdatemodified = BitConverter.ToInt64(datemodifiedbytes, 0);
            long numdatecreated = BitConverter.ToInt64(datecreatedbytes, 0);

            return new FileMetadata(Encoding.ASCII.GetString(filenamebytes),
                new DateTimeOffset(numdateaccessed, new TimeSpan(0L)),
                new DateTimeOffset(numdatemodified, new TimeSpan(0L)),
                new DateTimeOffset(numdatecreated, new TimeSpan(0L)),
                BitConverter.ToInt64(filesizebytes, 0),
                blockshashes);
        }
    }
}
