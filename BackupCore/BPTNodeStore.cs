using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Caching;
using System.IO;

namespace BackupCore
{
    class BPTNodeStore
    {
        private MemoryCache NodeCache { get; set; }

        private string NodeFolderPath { get; set; }

        public BPTNodeStore(string nodefolderpath)
        {
            NodeCache = MemoryCache.Default;
            NodeFolderPath = nodefolderpath;
        }

        public BPlusTreeNode GetNode(string nodeid)
        {
            CacheItem ci_node = NodeCache.GetCacheItem(nodeid);
            if (ci_node == null)
            {
                CacheItemPolicy policy = new CacheItemPolicy();
                List<string> filepaths = new List<string>();
                filepaths.Add(Path.Combine(NodeFolderPath, nodeid));
                policy.ChangeMonitors.Add(new HostFileChangeMonitor(filepaths));

                policy.RemovedCallback = new CacheEntryRemovedCallback(OnRemoval);

                BPlusTreeNode node = BPlusTree.DeserializeNode(Path.Combine(NodeFolderPath, nodeid));

                NodeCache.Set(nodeid, node, policy);

                // TODO: Use something like the limit physical memory option on Memory cache itself
                // instead of this
                if (NodeCache.GetCount() > 2000)
                {
                    NodeCache.Trim(10);
                }

                return node;
            }
            else
            {
                return (BPlusTreeNode)ci_node.Value;
            }
        }

        private void OnRemoval(CacheEntryRemovedArguments arguments)
        {
            BPlusTreeNode node = (BPlusTreeNode)arguments.CacheItem.Value;
            // Save needed
            if (node.Dirty)
            {
                BPlusTree.SerializeNode(node, Path.Combine(NodeFolderPath, node.NodeID));
            }
        }

    }
}
