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

        public BackupSet()
        {
            Backups = new List<(byte[] hash, bool shallow)>();
        }

        private BackupSet(List<(byte[], bool)> backups)
        {
            Backups = backups;
        }        

        public byte[] serialize()
        {
            // -"-v1"
            // backuphashes = enum_encode([Backups.backuphash,...])
            // shallowflags = enum_encode([BitConverter.GetBytes(Bakups.shallow),...])
            byte[] backuphashes = BinaryEncoding.enum_encode(from backup in Backups select backup.hash);
            byte[] shallowflags = BinaryEncoding.enum_encode(from backup in Backups select BitConverter.GetBytes(backup.shallow));
            return BinaryEncoding.dict_encode(new Dictionary<string, byte[]>
            {
                { "backuphashes-v1", backuphashes },
                { "shallowflags-v1", shallowflags }
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
            return new BackupSet(backups);
        }
    }
}
