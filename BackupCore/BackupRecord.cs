using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BackupRecord : ICustomSerializable<BackupRecord>
    {
        public DateTimeOffset BackupTime { get; set; }
        public string BackupMessage { get; set; }
        public List<byte[]> MetadataTreeHashes { get; set; }

        public BackupRecord(string message, List<byte[]> treehashes)
        {
            BackupTime = DateTimeOffset.Now;
            BackupMessage = message;
            MetadataTreeHashes = treehashes;
        }

        private BackupRecord(DateTimeOffset backuptime, string message, List<byte[]> treehashes)
        {
            BackupTime = backuptime;
            if (message == null)
            {
                message = "";
            }
            BackupMessage = message;
            MetadataTreeHashes = treehashes;
        }

        public byte[] serialize()
        {
            byte[] backuptimebytes = BitConverter.GetBytes(BackupTime.Ticks);
            byte[] backupmessagebytes = Encoding.ASCII.GetBytes(BackupMessage);

            List<byte> binrep = new List<byte>();

            BinaryEncoding.encode(backuptimebytes, binrep);
            BinaryEncoding.encode(backupmessagebytes, binrep);
            foreach (var hash in MetadataTreeHashes)
            {
                BinaryEncoding.encode(hash, binrep);
            }

            return binrep.ToArray();
        }

        public static BackupRecord deserialize(byte[] data)
        {
            byte[] backuptimebytes;
            byte[] backupmessagebytes;

            List<byte[]> savedobjects = BinaryEncoding.decode(data);
            backuptimebytes = savedobjects[0];
            backupmessagebytes = savedobjects[1];

            List<byte[]> treehashes = savedobjects.GetRange(2, savedobjects.Count - 2);

            long numbackuptime = BitConverter.ToInt64(backuptimebytes, 0);

            return new BackupRecord(new DateTimeOffset(numbackuptime, new TimeSpan(0L)),
                Encoding.ASCII.GetString(backupmessagebytes), treehashes);
        }
    }
}
