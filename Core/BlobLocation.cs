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
        public int BytePosition { get; set; }
        public int ByteLength { get; set; }
        public bool IsMultiBlockReference { get; set; }

        /// <summary>
        /// Maps backupset nmaes to their reference counts
        /// </summary>
        public Dictionary<string, int> BSetReferenceCounts { get; set; }

        public int TotalReferenceCount => BSetReferenceCounts.Values.Sum();

        public int TotalNonShallowReferenceCount => BSetReferenceCounts.Where(kvp => !kvp.Key.EndsWith(Core.ShallowSuffix)).Select(kvp => kvp.Value).Sum();

        public BlobLocation(string relpath, int bytepos, int bytelen, bool multiblock) : this(relpath, bytepos, bytelen, multiblock, new Dictionary<string, int>()) { }

        private BlobLocation(string relpath, int bytepos, int bytelen, bool multiblock, Dictionary<string, int> referencecounts)
        {
            RelativeFilePath = relpath;
            BytePosition = bytepos;
            ByteLength = bytelen;
            BSetReferenceCounts = referencecounts;
            IsMultiBlockReference = multiblock;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return RelativeFilePath == ((BlobLocation)obj).RelativeFilePath && BytePosition == ((BlobLocation)obj).BytePosition && 
                ByteLength == ((BlobLocation)obj).ByteLength && ((BlobLocation)obj).IsMultiBlockReference == IsMultiBlockReference;
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
            // -v6
            // BlobType removed
            // IsMultiBlockReference removed
            // -v7
            // IsMultiBlockReference readded

            bldata.Add("RelativeFilePath-v1", Encoding.UTF8.GetBytes(RelativeFilePath));
            bldata.Add("BytePosition-v1", BitConverter.GetBytes(BytePosition));
            bldata.Add("ByteLength-v1", BitConverter.GetBytes(ByteLength));

            bldata.Add("BSetReferenceCounts.BackupSets-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Keys.Select(set => Encoding.UTF8.GetBytes(set))));
            bldata.Add("BSetReferenceCounts.ReferenceCounts-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Values.Select(rc => BitConverter.GetBytes(rc))));

            bldata.Add("IsMultiBlockReference-v6", BitConverter.GetBytes(IsMultiBlockReference));

            return BinaryEncoding.dict_encode(bldata);
        }

        public static BlobLocation deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            string relfilepath = Encoding.UTF8.GetString(savedobjects["RelativeFilePath-v1"]);
            int byteposition = BitConverter.ToInt32(savedobjects["BytePosition-v1"], 0);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);
            
            var backupsets = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.BackupSets-v5"]).Select(bin => Encoding.UTF8.GetString(bin)).ToList();
            var referencecounts = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.ReferenceCounts-v5"]).Select(bin => BitConverter.ToInt32(bin, 0)).ToList();
            Dictionary<string, int> bsrc = new Dictionary<string, int>();
            for (int i = 0; i < backupsets.Count; i++)
            {
                bsrc[backupsets[i]] = referencecounts[i];
            }
            var multiblock = BitConverter.ToBoolean(savedobjects["IsMultiBlockReference-v6"], 0);
            return new BlobLocation(relfilepath, byteposition, bytelength, multiblock, bsrc);
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
