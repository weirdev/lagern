using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class CukooFilter : IMemberFilter<byte[]>
    {
        public CukooFilter()
        {
        }

        public CukooFilter(HashBin child1, HashBin child2)
        {
            
        }

        public bool AddItem(byte[] item)
        {
            throw new NotImplementedException();
        }

        public bool ContainsItem(byte[] item)
        {
            throw new NotImplementedException();
        }

        public bool RemoveItem(byte[] item)
        {
            throw new NotImplementedException();
        }
    }
}
