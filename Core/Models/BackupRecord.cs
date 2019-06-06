using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class BackupRecord : ICustomSerializable<BackupRecord>
    {
        public DateTime BackupTime { get; set; }
        // Prevent BackupMessage from being null
        private string _message;
        public string BackupMessage
        {
            get { return _message; }
            set { _message = value ?? ""; }
        }
        private byte[] UUID { get; set; }

        static Random UUIDGenerator = new Random();
        
        public byte[] MetadataTreeHash { get; set; }

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

        public byte[] serialize()
        {
            Dictionary<string, byte[]> brdata = new Dictionary<string, byte[]>();
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


            brdata.Add("BackupTime-v1", BitConverter.GetBytes(BackupTime.Ticks));
            brdata.Add("BackupMessage-v1", Encoding.UTF8.GetBytes(BackupMessage));

            brdata.Add("MetadataTreeHash-v2", MetadataTreeHash);

            brdata.Add("UUID-v3", UUID);

            return BinaryEncoding.dict_encode(brdata);
        }

        public static BackupRecord deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            DateTime backuptime = new DateTime(BitConverter.ToInt64(savedobjects["BackupTime-v1"], 0));
            string backupmessage;
            if (savedobjects["BackupMessage-v1"] != null)
            {
                backupmessage = Encoding.UTF8.GetString(savedobjects["BackupMessage-v1"]);
            }
            else
            {
                backupmessage = null;
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
    }
}
