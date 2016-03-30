using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Runtime.Serialization;
using System.Xml;
using System.Security.AccessControl;

namespace BackupCore
{
    [DataContract]
    public class BasicMetadata
    {
        [DataMember]
        public string FileName { get; set; }

        [IgnoreDataMember]
        public DateTimeOffset DateAccessed { get; set; }

        [DataMember]
        private long XmlDateAccessed
        {
            get { return DateAccessed.Ticks; }
            set { DateAccessed = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [IgnoreDataMember]
        public DateTimeOffset DateModified { get; set; }

        [DataMember]
        private long XmlDateModified
        {
            get { return DateModified.Ticks; }
            set { DateModified = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [IgnoreDataMember]
        public DateTimeOffset DateCreated { get; set; }

        [DataMember]
        private long XmlDateCreated
        {
            get { return DateCreated.Ticks; }
            set { DateCreated = new DateTimeOffset(value, new TimeSpan(0L)); }
        }

        [DataMember]
        public long FileSize { get; set; }

        [DataMember]
        public FileAttributes Attributes { get; set; }

        // TODO: Replace serializing ACL with writing ACL to a dummy file in a security store in backup location
        public FileSecurity AccessControl { get; set; }

        public BasicMetadata(string filename, DateTimeOffset dateaccessed, DateTimeOffset datemodified, 
            DateTimeOffset datecreated, long filesize, FileAttributes attributes, FileSecurity accesscontrol)
        {
            FileName = filename;
            DateAccessed = dateaccessed;
            DateModified = datemodified;
            FileSize = filesize;
            DateCreated = datecreated;
            Attributes = attributes;
            AccessControl = accesscontrol;
        }

        public BasicMetadata(string filepath)
        {
            FileName = Path.GetFileName(filepath);
            FileInfo fi = new FileInfo(filepath);
            DateAccessed = fi.LastAccessTimeUtc;
            DateModified = fi.LastWriteTimeUtc;
            FileSize = fi.Length;
            DateCreated = fi.CreationTimeUtc;
            Attributes = fi.Attributes;
            AccessControl = fi.GetAccessControl();
        }

        public void WriteOut(string filepath)
        {
            FileInfo fi = new FileInfo(filepath);
            fi.LastAccessTimeUtc = DateAccessed.DateTime;
            fi.LastWriteTimeUtc = DateModified.DateTime;
            fi.CreationTimeUtc = DateCreated.DateTime;
            fi.Attributes = Attributes;
            fi.SetAccessControl(AccessControl);
        }
    }
}
