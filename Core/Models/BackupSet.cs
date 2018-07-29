using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.IO;

namespace BackupCore
{
    public class BackupSet : ICustomSerializable<BackupSet>
    {
        // Backuphash is reference in blobs to BackupRecord
        // Backup is shallow if only metadata is stored for that backup
        public List<(byte[] hash, bool shallow)> Backups { get; private set; }

        public bool CacheUsed { get; set; }
        public bool Encrypted { get; set; }

        public BackupSet(bool cacheused, bool encrypted)
        {
            Backups = new List<(byte[] hash, bool shallow)>();
            CacheUsed = cacheused;
            Encrypted = encrypted;
        }

        private BackupSet(List<(byte[], bool)> backups, bool cacheused, bool encrypted)
        {
            Backups = backups;
            CacheUsed = cacheused;
            Encrypted = encrypted;
        }        

        public byte[] serialize()
        {
            // -"-v1"
            // backuphashes = enum_encode([Backups.backuphash,...])
            // shallowflags = enum_encode([BitConverter.GetBytes(Bakups.shallow),...])
            // -"-v2"
            // cacheused = BitConverter.GetBytes(CacheUsed)
            // -"-v3"
            // encrypted = BitConverter.GetBytes(Encrypted)
            byte[] backuphashes = BinaryEncoding.enum_encode(from backup in Backups select backup.hash);
            byte[] shallowflags = BinaryEncoding.enum_encode(from backup in Backups select BitConverter.GetBytes(backup.shallow));

            byte[] cacheused = BitConverter.GetBytes(CacheUsed);
            byte[] encrypted = BitConverter.GetBytes(Encrypted);

            return BinaryEncoding.dict_encode(new Dictionary<string, byte[]>
            {
                { "backuphashes-v1", backuphashes },
                { "shallowflags-v1", shallowflags },
                { "cacheused-v2", cacheused },
                { "encrypted-v3", encrypted }
            });
        }

        public static BackupSet deserialize(byte[] data)
        {
            Dictionary<string, byte[]> saved_objects = BinaryEncoding.dict_decode(data);
            List<byte[]> backuphashes = BinaryEncoding.enum_decode(saved_objects["backuphashes-v1"]);
            List<bool> shallowflags = new List<bool>(from bb in BinaryEncoding.enum_decode(saved_objects["shallowflags-v1"]) select BitConverter.ToBoolean(bb, 0));
            List<(byte[] hash, bool shallow)> backups = new List<(byte[], bool)>();
            for (int i = 0; i < backuphashes.Count; i++)
            {
                backups.Add((hash: backuphashes[i], shallow: shallowflags[i]));
            }
            bool cacheused = BitConverter.ToBoolean(saved_objects["cacheused-v2"], 0);
            bool encrypted = false;
            if (saved_objects.ContainsKey("encrypted-v3"))
            {
                encrypted = BitConverter.ToBoolean(saved_objects["encrypted-v3"], 0);
            }
            return new BackupSet(backups, cacheused, encrypted);
        }
    }
}
