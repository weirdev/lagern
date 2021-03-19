using LagernCore.Models;
using LagernCore.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BackupCore
{
    public class BlobLocation : ICustomSerializable<BlobLocation>
    {
        /// <summary>
        /// The hash under which the blob is stored on the destination,
        /// may or may not be actually encrypted
        /// </summary>
        public byte[]? EncryptedHash { get; set; }
        // TODO: change to blobID
        // blobID will initially correspond 1-1 with original hash of block
        // later blobs may be combined, and blobID will be the ID (filename)
        // of the new combination blob
        public string RelativeFilePath { get; set; }
        public int ByteLength { get; set; }
        public List<byte[]>? BlockHashes { get; set; }

        /// <summary>
        /// Maps backupset nmaes to their reference counts
        /// </summary>
        private Dictionary<BackupSetKey, int> BSetReferenceCounts { get; set; }

        public int TotalReferenceCount => BSetReferenceCounts.Values.Sum();

        public int TotalNonShallowReferenceCount => BSetReferenceCounts.Where(kvp => !kvp.Key.Shallow).Select(kvp => kvp.Value).Sum();

        public BlobLocation(byte[]? encryptedHash, string relpath, int bytelen) : this(encryptedHash, relpath, bytelen, null, new Dictionary<BackupSetKey, int>()) { }

        public BlobLocation(List<byte[]>? childhashes = null) : this(null, "", 0, childhashes, new Dictionary<BackupSetKey, int>()) { }

        private BlobLocation(byte[]? encryptedHash, string relpath, int bytelen, List<byte[]>? blockhashes, Dictionary<BackupSetKey, int> referencecounts)
        {
            EncryptedHash = encryptedHash;
            RelativeFilePath = relpath;
            ByteLength = bytelen;
            BSetReferenceCounts = referencecounts;
            BlockHashes = blockhashes;
        }

        public int? GetBSetReferenceCount(BackupSetReference backupSet)
        {
            if (BSetReferenceCounts.TryGetValue(new BackupSetKey(backupSet.BackupSetName, backupSet.Shallow, backupSet.BlobListCache), out int refCount))
            {
                return refCount;
            }
            return null;
        }

        public void SetBSetReferenceCount(BackupSetReference backupSet, int count)
        {
            BSetReferenceCounts[new BackupSetKey(backupSet.BackupSetName, backupSet.Shallow, backupSet.BlobListCache)] = count;
        }

        public void RemoveBSetReference(BackupSetReference backupSet)
        {
            BSetReferenceCounts.Remove(new BackupSetKey(backupSet.BackupSetName, backupSet.Shallow, backupSet.BlobListCache));
        }

        public override bool Equals(object? obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            BlobLocation other = (BlobLocation) obj;
            return RelativeFilePath == other.RelativeFilePath &&
                ((other.EncryptedHash == null || EncryptedHash == null) ?
                        other.EncryptedHash == null && EncryptedHash == null :
                        other.EncryptedHash.SequenceEqual(EncryptedHash)) &&
                ByteLength == other.ByteLength &&
                ((other.BlockHashes == null || BlockHashes == null) ?
                        other.BlockHashes == null && BlockHashes == null :
                        other.BlockHashes.DeepSequenceEqual(BlockHashes));
        }

        public override int GetHashCode()
        {
            return (EncryptedHash?.GetHashCode().ToString() + RelativeFilePath.ToString() + ByteLength.ToString()).GetHashCode();
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> bldata = new Dictionary<string, byte[]>();
            // -"-v1"
            // RelativeFilePath = Encoding.UTF8.GetBytes(string)
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

            bldata.Add("BSetReferenceCounts.BackupSets-v5", BinaryEncoding.enum_encode(BSetReferenceCounts.Keys.Select(bsr => bsr.serialize())));
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
            byte[]? encryptedHash = savedobjects["EncryptedHash-v9"].Length == 0 ? null : savedobjects["EncryptedHash-v9"];
            string relfilepath = Encoding.UTF8.GetString(savedobjects["RelativeFilePath-v1"]);
            int bytelength = BitConverter.ToInt32(savedobjects["ByteLength-v1"], 0);
            
            var encodedBackupSets = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.BackupSets-v5"]);
            if (encodedBackupSets == null)
            {
                throw new Exception("Backup sets are required");
            }
            List<BackupSetKey> backupsets = new List<BackupSetKey>();
            foreach (var bin in encodedBackupSets)
            {
                if (bin == null)
                {
                    throw new Exception("Backup sets cannot be null");
                }
                backupsets.Add(BackupSetKey.decode(bin));
            }

            List<byte[]?>? encodedRefCounts = BinaryEncoding.enum_decode(savedobjects["BSetReferenceCounts.ReferenceCounts-v5"]);
            if (encodedRefCounts == null)
            {
                throw new Exception("Reference counts are required");
            }
            List<int> referencecounts = new List<int>();
            foreach (var bin in encodedRefCounts)
            {
                if (bin == null)
                {
                    throw new Exception("Reference counts cannot be null");
                }
                referencecounts.Add(BitConverter.ToInt32(bin, 0));
            }
            Dictionary<BackupSetKey, int> bsrc = new Dictionary<BackupSetKey, int>();
            for (int i = 0; i < backupsets.Count; i++)
            {
                bsrc[backupsets[i]] = referencecounts[i];
            }
            var multiblock = BitConverter.ToBoolean(savedobjects["IsMultiBlockReference-v7"], 0);
            List<byte[]>? childhashes = null;
            if (multiblock)
            {
                childhashes = new List<byte[]>();
                var binchildhashes = BinaryEncoding.enum_decode(savedobjects["BlockHashes-v8"]);
                if (binchildhashes == null)
                {
                    throw new Exception("Multiblock blobs must have child hashes");
                }
                foreach (var bin in binchildhashes)
                {
                    if (bin == null)
                    {
                        throw new Exception("Child hashes cannot be null");
                    }
                    childhashes.Add(bin);
                }
            }

            return new BlobLocation(encryptedHash, relfilepath, bytelength, childhashes, bsrc);
        }

        public enum BlobType
        {
            Simple=0,
            FileBlob=1,
            BackupRecord=3,
            MetadataNode=4
            // NOTE: MUST update BlobStore.GetAllBlobReferences() for new blob types that reference other blobs
        }

        public record BackupSetKey(String BackupSetName, bool Shallow, bool BlobListCache)
        {
            public byte[] serialize()
            {
                Dictionary<string, byte[]> bsrData = new Dictionary<string, byte[]>
                {
                    // -"-v1"
                    // Required:
                    // BackupSet = Encoding.UTF8.GetBytes(string)
                    // Shallow = BitConverter.GetBytes(bool)
                    // BlobListCache = BitConverter.GetBytes(bool)
                    { "BackupSet-v1", Encoding.UTF8.GetBytes(BackupSetName) },
                    { "Shallow-v1", BitConverter.GetBytes(Shallow) },
                    { "BlobListCache-v1", BitConverter.GetBytes(BlobListCache) }
                };

                return BinaryEncoding.dict_encode(bsrData);
            }

            public static BackupSetKey decode(byte[] data)
            {
                Dictionary<string, byte[]> bsrData = BinaryEncoding.dict_decode(data);

                string bset = Encoding.UTF8.GetString(bsrData["BackupSet-v1"]);
                bool shallow = BitConverter.ToBoolean(bsrData["Shallow-v1"]);
                bool blobListCache = BitConverter.ToBoolean(bsrData["BlobListCache-v1"]);

                return new BackupSetKey(bset, shallow, blobListCache);
            }
        }
    }
}
