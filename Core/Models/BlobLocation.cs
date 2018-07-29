using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BackupCore
{
    public class BlobLocation : ICustomSerializable<BlobLocation>
    {
        /// <summary>
        /// The hash under which the blob is stored on the destination,
        /// may or may not be actually encrypted
        /// </summary>
        public byte[] EncryptedHash { get; set; }
        // TODO: change to blobID
        // blobID will initially correspond 1-1 with original hash of block
        // later blobs may be combined, and blobID will be the ID (filename)
        // of the new combination blob
        public string RelativeFilePath { get; set; }
        public int ByteLength { get; set; }
        public List<byte[]> BlockHashes { get; set; }

        /// <summary>
        /// Maps backupset nmaes to their reference counts
        /// </summary>
        public Dictionary<string, int> BSetReferenceCounts { get; set; }

        public int TotalReferenceCount => BSetReferenceCounts.Values.Sum();

        public int TotalNonShallowReferenceCount => BSetReferenceCounts.Where(kvp => !kvp.Key.EndsWith(Core.ShallowSuffix)).Select(kvp => kvp.Value).Sum();

        public BlobLocation(byte[] encryptedHash, string relpath, int bytelen) : this(encryptedHash, relpath, bytelen, null, new Dictionary<string, int>()) { }

        public BlobLocation(List<byte[]> childhashes = null) : this(null, "", 0, childhashes, new Dictionary<string, int>()) { }

        private BlobLocation(byte[] encryptedHash, string relpath, int bytelen, List<byte[]> blockhashes, Dictionary<string, int> referencecounts)
        {
            EncryptedHash = encryptedHash;
            RelativeFilePath = relpath;
            ByteLength = bytelen;
            BSetReferenceCounts = referencecounts;
            BlockHashes = blockhashes;
        }

        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return RelativeFilePath == ((BlobLocation)obj).RelativeFilePath && ((((BlobLocation)obj).EncryptedHash == null && EncryptedHash == null)
                || ((BlobLocation)obj).EncryptedHash.SequenceEqual(EncryptedHash)) && 
                ByteLength == ((BlobLocation)obj).ByteLength && ((((BlobLocation)obj).BlockHashes == null && BlockHashes == null)
                || ((BlobLocation)obj).BlockHashes.SequenceEqual(BlockHashes));
        }

        public override int GetHashCode() => (EncryptedHash.GetHashCode().ToString() + RelativeFilePath.ToString() + ByteLength.ToString()).GetHashCode();

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
            // -v8
            // BlockHashes = enum_encode
            // -v9
            // EncryptedHash = bytes

            bldata.Add("EncryptedHash-v9", EncryptedHash == null ? new byte[0] : EncryptedHash);
            bldata.Add("RelativeFilePath-v1", Encoding.UTF8.GetBytes(RelativeFilePath));
            bldata.Add("ByteLength-v1", BitConverter.GetBytes(ByteLength));

            bldata.Add("BSetReferenceCounts.BackupSets-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Keys.Select(set => Encoding.UTF8.GetBytes(set))));
            bldata.Add("BSetReferenceCounts.ReferenceCounts-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Values.Select(rc => BitConverter.GetBytes(rc))));

            bldata.Add("IsMultiBlockReference-v7", BitConverter.GetBytes(BlockHashes != null));
            if (BlockHashes != null)
            {
                bldata.Add("BlockHashes-v8", BinaryEncoding.enum_encode(BlockHashes));
            }

            return BinaryEncoding.dict_encode(bldata);
        }

        public static BlobLocation deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            byte[] encryptedHash = savedobjects["EncryptedHash-v9"].Length == 0 ? null : savedobjects["EncryptedHash-v9"];
            string relfilepath = Encoding.UTF8.GetString(savedobjects["RelativeFilePath-v1"]);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);
            
            var backupsets = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.BackupSets-v5"]).Select(bin => Encoding.UTF8.GetString(bin)).ToList();
            var referencecounts = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.ReferenceCounts-v5"]).Select(bin => BitConverter.ToInt32(bin, 0)).ToList();
            Dictionary<string, int> bsrc = new Dictionary<string, int>();
            for (int i = 0; i < backupsets.Count; i++)
            {
                bsrc[backupsets[i]] = referencecounts[i];
            }
            var multiblock = BitConverter.ToBoolean(savedobjects["IsMultiBlockReference-v7"], 0);
            List<byte[]> childhashes = null;
            if (multiblock)
            {
                childhashes = BinaryEncoding.enum_decode(savedobjects["BlockHashes-v8"]).ToList();
            }
            return new BlobLocation(encryptedHash, relfilepath, bytelength, childhashes, bsrc);
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
