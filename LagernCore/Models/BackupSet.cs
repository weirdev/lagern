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

        public BackupSet(bool cacheused)
        {
            Backups = new List<(byte[] hash, bool shallow)>();
            CacheUsed = cacheused;
        }

        private BackupSet(List<(byte[], bool)> backups, bool cacheused)
        {
            Backups = backups;
            CacheUsed = cacheused;
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
            // -v4
            // removed encrypted

            byte[] backuphashes = BinaryEncoding.enum_encode(from backup in Backups select backup.hash);
            byte[] shallowflags = BinaryEncoding.enum_encode(from backup in Backups select BitConverter.GetBytes(backup.shallow));

            byte[] cacheused = BitConverter.GetBytes(CacheUsed);

            return BinaryEncoding.dict_encode(new Dictionary<string, byte[]>
            {
                { "backuphashes-v1", backuphashes },
                { "shallowflags-v1", shallowflags },
                { "cacheused-v2", cacheused }
            });
        }

        public static BackupSet deserialize(byte[] data)
        {
            Dictionary<string, byte[]> saved_objects = BinaryEncoding.dict_decode(data);
            List<byte[]?> backuphashes = BinaryEncoding.enum_decode(saved_objects["backuphashes-v1"]) ?? new List<byte[]?>();
            List<bool> shallowflags = new List<bool>(from bb in BinaryEncoding.enum_decode(saved_objects["shallowflags-v1"]) select BitConverter.ToBoolean(bb, 0));
            List<(byte[] hash, bool shallow)> backups = new List<(byte[], bool)>();
            for (int i = 0; i < backuphashes.Count; i++)
            {
                var backuphash = backuphashes[i] ?? throw new NullReferenceException("Backup hash cannot be null");
                backups.Add((hash: backuphash, shallow: shallowflags[i]));
            }
            bool cacheused = BitConverter.ToBoolean(saved_objects["cacheused-v2"], 0);
            return new BackupSet(backups, cacheused);
        }
    }
}
