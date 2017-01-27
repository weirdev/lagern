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
        public List<byte[]> MetadataTreeHashes { get; set; }

        public BackupRecord(string message, List<byte[]> treehashes)
        {
            BackupTime = DateTime.UtcNow;
            BackupMessage = message;
            MetadataTreeHashes = treehashes;
        }

        private BackupRecord(DateTime backuptime, string message, List<byte[]> treehashes)
        {
            BackupTime = backuptime;
            BackupMessage = message;
            MetadataTreeHashes = treehashes;
        }

        public byte[] serialize()
        {
            Dictionary<string, byte[]> brdata = new Dictionary<string, byte[]>();
            // -"-v1"
            // BackupTime = DateTime.Ticks
            // BackupMessage = Encoding.ASCII.GetBytes(string)
            // MetadataTreeHashes = enum_encode(List<byte[]>)
            
            brdata.Add("BackupTime-v1", BitConverter.GetBytes(BackupTime.Ticks));
            brdata.Add("BackupMessage-v1", Encoding.ASCII.GetBytes(BackupMessage));
            brdata.Add("MetadataTreeHashes-v1", BinaryEncoding.enum_encode(MetadataTreeHashes));
            
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
            backupmessage = null;
            List<byte[]> metadatatreehashes = BinaryEncoding.enum_decode(savedobjects["MetadataTreeHashes-v1"]);

            return new BackupRecord(backuptime, backupmessage, metadatatreehashes);
        }
    }
}
