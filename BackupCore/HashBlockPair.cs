using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public class HashBlockPair
    {
        public byte[] Hash { get; set; }
        public byte[] Block { get; set; }

        public HashBlockPair(byte[] hash, byte[] block)
        {
            Hash = hash;
            Block = block;
        }
    }
}
