using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BackupCore
{
    public class BlobLocation : ICustomSerializable<BlobLocation>
    {
        // TODO: change to blobID
        // blobID will initially correspond 1-1 with original hash of block
        // later blobs may be combined, and blobID will be the ID (filename)
        // of the new combination blob
        public string RelativeFilePath { get; set; }
        public BlobTypes BlobType { get; set; }
        public int BytePosition { get; set; }
        public int ByteLength { get; set; }
        public int ReferenceCount { get; set; }

        public BlobLocation(BlobTypes blobtype, string relpath, int bytepos, int bytelen) : this(blobtype, relpath, bytepos, bytelen, 1) { }

        private BlobLocation(BlobTypes blobtype, string relpath, int bytepos, int bytelen, int referencecount)
        {
            RelativeFilePath = relpath;
            BytePosition = bytepos;
            ByteLength = bytelen;
            ReferenceCount = referencecount;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return RelativeFilePath == ((BlobLocation)obj).RelativeFilePath && BytePosition == ((BlobLocation)obj).BytePosition && 
                ByteLength == ((BlobLocation)obj).ByteLength;
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
            // -"-v2"
            // BlobType = BitConverter.GetBytes(int)
            // -v3
            // Required: breaks compatability
            // ReferenceCount = BitConverter.GetBytes(int)

            bldata.Add("RelativeFilePath-v1", Encoding.ASCII.GetBytes(RelativeFilePath));
            bldata.Add("BytePosition-v1", BitConverter.GetBytes(BytePosition));
            bldata.Add("ByteLength-v1", BitConverter.GetBytes(ByteLength));

            bldata.Add("BlobType-v2", BitConverter.GetBytes(ByteLength));

            bldata.Add("ReferenceCount-v3", BitConverter.GetBytes(ReferenceCount));

            return BinaryEncoding.dict_encode(bldata);
        }

        public static BlobLocation deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            string relfilepath = Encoding.ASCII.GetString(savedobjects["RelativeFilePath-v1"]);
            int byteposition = BitConverter.ToInt32(savedobjects["BytePosition-v1"], 0);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);

            int blobtypeint = BitConverter.ToInt32(savedobjects["BlobType-v2"], 0);

            int referencecount = BitConverter.ToInt32(savedobjects["ReferenceCount-v3"], 0);

            return new BlobLocation((BlobTypes)blobtypeint, relfilepath, byteposition, bytelength, referencecount);
        }

        public enum BlobTypes
        {
            FileBlock=0,
            HashList=1
        }
    }
}
