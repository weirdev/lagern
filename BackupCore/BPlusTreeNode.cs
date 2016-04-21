using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    class BPlusTreeNode
    {
        public BPlusTreeNode Parent { get; set; }
        // Only for leaf nodes makes a linked list for efficient in-order traversal
        public BPlusTreeNode Next { get; set; }

        public int NodeSize { get; set; }

        public List<byte[]> Keys { get; set; }

        public bool IsLeafNode { get; set; }
        
        // Size 100
        public List<BPlusTreeNode> Children { get; set; }
        
        // Size 99
        public List<BackupLocation> Values { get; set; }

        public BPlusTreeNode(BPlusTreeNode parent, bool isleafnode, int nodesize, BPlusTreeNode next=null)
        {
            Parent = parent;
            NodeSize = nodesize;
            IsLeafNode = isleafnode;
            Keys = new List<byte[]>();
            if (IsLeafNode)
            {
                Values = new List<BackupLocation>();
                Next = next;
            }
            else
            {
                Children = new List<BPlusTreeNode>();
            }
        }
    }
}
