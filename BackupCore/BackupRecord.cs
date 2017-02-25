using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BackupRecord : ICustomSerializable<BackupRecord>
    {
        public DateTime BackupTime { get; set; }
        // Prevent BackupMessage from being null
        private string _message;
        public string BackupMessage
        {
            get { return _message; }
            set { _message = value == null ? "" : value; }
        }
        public byte[] MetadataTreeHash { get; set; }

        public BackupRecord(string message, byte[] treehash)
        {
            BackupTime = DateTime.UtcNow;
            BackupMessage = message;
            MetadataTreeHash = treehash;
        }

        private BackupRecord(DateTime backuptime, string message, byte[] treehash)
        {
            BackupTime = backuptime;
            BackupMessage = message;
            MetadataTreeHash = treehash;
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> brdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // BackupTime = DateTime.Ticks
            // BackupMessage = Encoding.ASCII.GetBytes(string)
            // MetadataTreeHashes = enum_encode(List<byte[]>)

            // -v2
            // Breaks compatability
            // v1 - MetadataTreeHashes +
            // MetadataTreeHash = byte[]
            
            brdata.Add("BackupTime-v1", BitConverter.GetBytes(BackupTime.Ticks));
            brdata.Add("BackupMessage-v1", Encoding.ASCII.GetBytes(BackupMessage));

            brdata.Add("MetadataTreeHash-v2", MetadataTreeHash);
            
            return BinaryEncoding.dict_encode(brdata);
        }

        public static BackupRecord deserialize(byte[] data)
        {
            Dictionary<string, byte[]> savedobjects = BinaryEncoding.dict_decode(data);
            DateTime backuptime = new DateTime(BitConverter.ToInt64(savedobjects["BackupTime-v1"], 0));
            string backupmessage;
            if (savedobjects["BackupMessage-v1"] != null)
            {
                backupmessage = Encoding.ASCII.GetString(savedobjects["BackupMessage-v1"]);
            }
            else
            {
                backupmessage = null;
            }
            byte[] metadatatreehash = savedobjects["MetadataTreeHash-v2"];

            return new BackupRecord(backuptime, backupmessage, metadatatreehash);
        }
    }
}
