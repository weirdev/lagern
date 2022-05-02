using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace BackupCore
{
    public class BackupRecord : ICustomByteTransformable<BackupRecord>, IEquatable<BackupRecord?>
    {
        public DateTime BackupTime { get; private set; }
        public string BackupMessage { get; private set; }
        private byte[] UUID { get; set; }

        private static readonly Random UUIDGenerator = new();
        
        public byte[] MetadataTreeHash { get; private set; }

        public BackupRecord(string message, byte[] treehash, DateTime backupTime)
        {
            BackupTime = backupTime;
            BackupMessage = message;
            MetadataTreeHash = treehash;
            UUID = new byte[16];
            UUIDGenerator.NextBytes(UUID);
        }

        private BackupRecord(DateTime backuptime, string message, byte[] treehash, byte[] uuid)
        {
            BackupTime = backuptime;
            BackupMessage = message;
            MetadataTreeHash = treehash;
            UUID = uuid;
        }

        public byte[] Serialize()
        {
            Dictionary<string, byte[]> brdata = new()
            {
                // -"-v1"
                // BackupTime = DateTime.Ticks
                // BackupMessage = Encoding.UTF8.GetBytes(string)
                // MetadataTreeHashes = enum_encode(List<byte[]>)

                // -v2
                // Breaks compatability
                // v1 - MetadataTreeHashes +
                // MetadataTreeHash = byte[]

                // -v3
                // v2 +
                // UUID = byte[]

                // -v4
                // MetadataTreeMultiBlock = BitConverter.GetBytes(bool)
                // -v5
                // removed MetadataTreeMultiBlock


                { "BackupTime-v1", BitConverter.GetBytes(BackupTime.Ticks) },
                { "BackupMessage-v1", Encoding.UTF8.GetBytes(BackupMessage) },

                { "MetadataTreeHash-v2", MetadataTreeHash },

                { "UUID-v3", UUID }
            };

            return BinaryEncoding.dict_encode(brdata);
        }

        public static BackupRecord Deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            DateTime backuptime = new(BitConverter.ToInt64(savedobjects["BackupTime-v1"], 0));
            string backupmessage;
            if (savedobjects["BackupMessage-v1"] != null)
            {
                backupmessage = Encoding.UTF8.GetString(savedobjects["BackupMessage-v1"]);
            }
            else
            {
                backupmessage = "";
            }

            byte[] metadatatreehash = savedobjects["MetadataTreeHash-v2"];

            byte[] uuid;
            if (savedobjects.ContainsKey("UUID-v3"))
            {
                uuid = savedobjects["UUID-v3"];
            }
            else
            {
                uuid = new byte[16];
                UUIDGenerator.NextBytes(uuid);
            }
                        
            return new BackupRecord(backuptime, backupmessage, metadatatreehash, uuid);
        }

        public override bool Equals(object? obj)
        {
            return Equals(obj as BackupRecord);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(BackupTime, BackupMessage, new BigInteger(UUID).GetHashCode(), 
                new BigInteger(MetadataTreeHash).GetHashCode());
        }

        public bool Equals(BackupRecord? other)
        {
            return other != null &&
                   BackupTime == other.BackupTime &&
                   BackupMessage == other.BackupMessage &&
                   UUID.SequenceEqual(other.UUID) &&
                   MetadataTreeHash.SequenceEqual(other.MetadataTreeHash);
        }

        public static bool operator ==(BackupRecord? left, BackupRecord? right)
        {
            return EqualityComparer<BackupRecord>.Default.Equals(left, right);
        }

        public static bool operator !=(BackupRecord? left, BackupRecord? right)
        {
            return !(left == right);
        }
    }
}
