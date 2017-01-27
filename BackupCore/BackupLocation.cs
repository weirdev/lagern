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
            Dictionary<string, byte[]> bldata = new Dictionary<string, byte[]>();
            // -"-v1"
            // RelativeFilePath = ASCII encoded
            // BytePosition = BitConverter.GetBytes(int)
            // ByteLength = BitConverter.GetBytes(int)

            bldata.Add("RelativeFilePath-v1", Encoding.ASCII.GetBytes(RelativeFilePath));
            bldata.Add("BytePosition-v1", BitConverter.GetBytes(BytePosition));
            bldata.Add("ByteLength-v1", BitConverter.GetBytes(ByteLength));
            
            return BinaryEncoding.dict_encode(bldata);
        }

        public static BackupLocation deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            string relfilepath = Encoding.ASCII.GetString(savedobjects["RelativeFilePath-v1"]);
            int byteposition = BitConverter.ToInt32(savedobjects["BytePosition-v1"], 0);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);

            return new BackupLocation(relfilepath, byteposition, bytelength);
        }
    }
}
