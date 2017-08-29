using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupConsole
{
    class Utilities
    {
        public static readonly string[] suffixes = new string[] { "B", "KB", "MB", "GB", "TB", "EB" };

        public static string BytesFormatter(int bytecount)
        {
            int suffix = 0;
            double bytes = bytecount;
            while (bytes > 1024)
            {
                bytes /= 1024;
                suffix += 1;
            }
            return bytes.ToString("G4") + suffixes[suffix];
        }
    }
}
