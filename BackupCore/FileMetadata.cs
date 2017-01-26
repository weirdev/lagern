using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Xml;
using System.Security.AccessControl;

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
        
        public long FileSize { get; set; }
        
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
        public FileMetadata(string filename, DateTime dateaccessed, DateTime datemodified, 
            DateTime datecreated, long filesize, List<byte[]> blockshashes)
        {
            FileName = filename;
            DateAccessedUTC = dateaccessed;
            DateModifiedUTC = datemodified;
            FileSize = filesize;
            DateCreatedUTC = datecreated;
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
            DateAccessedUTC = fi.LastAccessTimeUtc;
            DateModifiedUTC = fi.LastWriteTimeUtc;
            if (fi.Attributes == FileAttributes.Directory)
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
            FileInfo fi = new FileInfo(filepath);
            fi.LastAccessTimeUtc = DateAccessedUTC;
            fi.LastWriteTimeUtc = DateModifiedUTC;
            fi.CreationTimeUtc = DateCreatedUTC;
        }

        public byte[] serialize()
        {
            byte[] filenamebytes = Encoding.ASCII.GetBytes(FileName);
            byte[] dateaccessedbytes = BitConverter.GetBytes(NumDateAccessedUTC);
            byte[] datemodifiedbytes = BitConverter.GetBytes(NumDateModifiedUTC);
            byte[] datecreatedbytes = BitConverter.GetBytes(NumDateCreatedUTC);
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
                new DateTime(numdateaccessed),
                new DateTime(numdatemodified),
                new DateTime(numdatecreated),
                BitConverter.ToInt64(filesizebytes, 0),
                blockshashes);
        }
    }
}
