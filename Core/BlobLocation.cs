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
        /// <summary>
        /// Is this Blob comprised of several blocks? i.e. Is this block a list of hashes referencing blocks that make up this Blob?
        /// </summary>
        public bool IsMultiBlobReference { get; set; }
        public int BytePosition { get; set; }
        public int ByteLength { get; set; }

        /// <summary>
        /// Maps backupset nmaes to their reference counts
        /// </summary>
        public Dictionary<string, int> BSetReferenceCounts { get; set; }

        public int TotalReferenceCount { get { return BSetReferenceCounts.Values.Sum(); } }

        public BlobLocation(BlobTypes blobtype, bool ismultiblockref, string relpath, int bytepos, int bytelen) : this(blobtype, ismultiblockref, relpath, bytepos, bytelen, new Dictionary<string, int>()) { }

        private BlobLocation(BlobTypes blobtype, bool ismultiblockref, string relpath, int bytepos, int bytelen, Dictionary<string, int> referencecounts)
        {
            BlobType = blobtype;
            RelativeFilePath = relpath;
            IsMultiBlobReference = ismultiblockref;
            BytePosition = bytepos;
            ByteLength = bytelen;
            BSetReferenceCounts = referencecounts;
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

        public override int GetHashCode() => (BytePosition.ToString() + RelativeFilePath.ToString() + ByteLength.ToString()).GetHashCode();

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bldata = new Dictionary<string, byte[]>();
            // -"-v1"
            // RelativeFilePath = UTF8 encoded
            // BytePosition = BitConverter.GetBytes(int)
            // ByteLength = BitConverter.GetBytes(int)
            // -"-v2"
            // BlobType = BitConverter.GetBytes(int)
            // -v3
            // Required: breaks compatability
            // ReferenceCount = BitConverter.GetBytes(int)
            // -v4
            // Required
            // IsMultiBlockReference = BitConverter.GetBytes(bool)
            // -v5
            // Required
            // BSetReferenceCounts.BackupSets = 
            // BSetReferenceCounts.ReferenceCounts = 

            bldata.Add("RelativeFilePath-v1", Encoding.UTF8.GetBytes(RelativeFilePath));
            bldata.Add("BytePosition-v1", BitConverter.GetBytes(BytePosition));
            bldata.Add("ByteLength-v1", BitConverter.GetBytes(ByteLength));

            bldata.Add("BlobType-v2", BitConverter.GetBytes((int)BlobType));
            
            bldata.Add("IsMultiBlockReference-v4", BitConverter.GetBytes(IsMultiBlobReference));

            bldata.Add("BSetReferenceCounts.BackupSets-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Keys.Select(set => Encoding.UTF8.GetBytes(set))));
            bldata.Add("BSetReferenceCounts.ReferenceCounts-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Values.Select(rc => BitConverter.GetBytes(rc))));

            return BinaryEncoding.dict_encode(bldata);
        }

        public static BlobLocation deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            string relfilepath = Encoding.UTF8.GetString(savedobjects["RelativeFilePath-v1"]);
            int byteposition = BitConverter.ToInt32(savedobjects["BytePosition-v1"], 0);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);

            int blobtypeint = BitConverter.ToInt32(savedobjects["BlobType-v2"], 0);

            bool ismultiblockref = BitConverter.ToBoolean(savedobjects["IsMultiBlockReference-v4"], 0);

            var backupsets = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.BackupSets-v5"]).Select(bin => Encoding.UTF8.GetString(bin)).ToList();
            var referencecounts = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.ReferenceCounts-v5"]).Select(bin => BitConverter.ToInt32(bin, 0)).ToList();
            Dictionary<string, int> bsrc = new Dictionary<string, int>();
            for (int i = 0; i < backupsets.Count; i++)
            {
                bsrc[backupsets[i]] = referencecounts[i];
            }

            return new BlobLocation((BlobTypes)blobtypeint, ismultiblockref, relfilepath, byteposition, bytelength, bsrc);
        }

        public enum BlobTypes
        {
            Simple=0,
            FileBlob=1,
            BackupRecord=3,
            MetadataNode=4
            // NOTE: MUST update BlobStore.GetAllBlobReferences() for new blob types that reference other blobs
        }
    }
}
