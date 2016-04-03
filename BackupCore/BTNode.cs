using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BTNode<I, L>
    {
        public BTNode<I, L> Parent { get; set; }
        public BTNode<I, L> LeftChild { get; set; }
        public BTNode<I, L> RightChild { get; set; }

        public I InteriorValue { get; set; }
        public L LeafValue { get; set; }
    }
}
