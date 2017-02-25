using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class HashBin
    {
        public bool AddHash(byte[] hash, BlobLocation blocation)
        {
            throw new NotImplementedException();
        }

        public bool ContainsHash(byte[] hash)
        {
            throw new NotImplementedException();
        }

        public BlobLocation GetBackupLocation(byte[] hash)
        {
            throw new NotImplementedException();
        }
        
        // Returns half of this bin's hashes in a new bin
        // And removes those hashes from this bin
        public HashBin TakeHalf()
        {
            throw new NotImplementedException();
        }
    }
}
