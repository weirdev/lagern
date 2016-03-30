using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;

namespace BackupCore
{
    [DataContract(Name = "BackupLocation")]
    public class BackupLocation
    {
        [DataMember]
        public string RelativeFilePath { get; set; }
        [DataMember]
        public int BytePosition { get; set; }
        [DataMember]
        public int ByteLength { get; set; }

        public BackupLocation(string relpath, int bytepos, int bytelen)
        {
            RelativeFilePath = relpath;
            BytePosition = bytepos;
            ByteLength = bytelen;
        }
        
        public override bool Equals(object obj)
        {

            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return RelativeFilePath == ((BackupLocation)obj).RelativeFilePath && BytePosition == ((BackupLocation)obj).BytePosition && 
                ByteLength == ((BackupLocation)obj).ByteLength;
        }
        
        public override int GetHashCode()
        {
            return (BytePosition.ToString() + RelativeFilePath.ToString() + ByteLength.ToString()).GetHashCode();
        }
    }
}
