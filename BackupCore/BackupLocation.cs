using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BackupCore
{
    public class BackupLocation : ICustomSerializable<BackupLocation>
    {
        // TODO: change to blobID
        // blobID will initially correspond 1-1 with original hash of block
        // later blobs may be combined, and blobID will be the ID (filename)
        // of the new combination blob
        public string RelativeFilePath { get; set; }
        public int BytePosition { get; set; }
        public int ByteLength { get; set; }

        public BackupLocation(string relpath, int bytepos, int bytelen)
        {
            RelativeFilePath = relpath;
            BytePosition = bytepos;
            ByteLength = bytelen;
        }
        
        public override bool Equals(object obj)
        {

            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return RelativeFilePath == ((BackupLocation)obj).RelativeFilePath && BytePosition == ((BackupLocation)obj).BytePosition && 
                ByteLength == ((BackupLocation)obj).ByteLength;
        }
        
        public override int GetHashCode()
        {
            return (BytePosition.ToString() + RelativeFilePath.ToString() + ByteLength.ToString()).GetHashCode();
        }

        public byte[] serialize()
        {
            byte[] stringbytes = Encoding.ASCII.GetBytes(RelativeFilePath);
            byte[] bytepositionbytes = BitConverter.GetBytes(BytePosition);
            byte[] bytelengthbytes = BitConverter.GetBytes(ByteLength);

            List<byte> binrep = new List<byte>();
             
            BinaryEncoding.encode(stringbytes, binrep);
            BinaryEncoding.encode(bytepositionbytes, binrep);
            BinaryEncoding.encode(bytelengthbytes, binrep);

            return binrep.ToArray();
        }

        public static BackupLocation deserialize(byte[] data)
        {
            byte[] stringbytes;
            byte[] bytepositionbytes;
            byte[] bytelengthbytes;

            List<byte[]> savedobjects = BinaryEncoding.decode(data);
            stringbytes = savedobjects[0];
            bytepositionbytes = savedobjects[1];
            bytelengthbytes = savedobjects[2];
            
            return new BackupLocation(Encoding.ASCII.GetString(stringbytes),
                BitConverter.ToInt32(bytepositionbytes, 0),
                BitConverter.ToInt32(bytelengthbytes, 0));
        }
    }
}
