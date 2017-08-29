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
        public List<Tuple<byte[], bool>> Backups { get; private set; }

        public BackupSet()
        {
            Backups = new List<Tuple<byte[], bool>>();
        }

        private BackupSet(List<Tuple<byte[], bool>> backups)
        {
            Backups = backups;
        }        

        public byte[] serialize()
        {
            // -"-v1"
            // backuphashes = enum_encode([Backups.backuphash,...])
            // shallowflags = enum_encode([BitConverter.GetBytes(Bakups.shallow),...])
            byte[] backuphashes = BinaryEncoding.enum_encode(from backup in Backups select backup.Item1);
            byte[] shallowflags = BinaryEncoding.enum_encode(from backup in Backups select BitConverter.GetBytes(backup.Item2));
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
            List<Tuple<byte[], bool>> backups = new List<Tuple<byte[], bool>>();
            for (int i = 0; i < backuphashes.Count; i++)
            {
                backups.Add(new Tuple<byte[], bool>(backuphashes[i], shallowflags[i]));
            }
            return new BackupSet(backups);
        }
    }
}
