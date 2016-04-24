using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Caching;
using System.IO;
using System.Collections.Specialized;

namespace BackupCore
{
    class BPTNodeStore
    {
        private MemoryCache NodeCache { get; set; }

        private string NodeFolderPath { get; set; }

        public BPTNodeStore(string nodefolderpath)
        {
            NameValueCollection CacheSettings = new NameValueCollection(2);
            CacheSettings.Add("physicalMemoryLimitPercentage", Convert.ToString(70));  //set % here
            CacheSettings.Add("pollingInterval", Convert.ToString("00:00:05"));

            NodeCache = new MemoryCache("NodeCache", CacheSettings);
            
            NodeFolderPath = nodefolderpath;
        }

        public BPlusTreeNode GetNode(string nodeid)
        {
            CacheItem ci_node = NodeCache.GetCacheItem(nodeid);
            if (ci_node == null)
            {
                CacheItemPolicy policy = new CacheItemPolicy();

                // NOTE: shouldn't need to monitor node files because nothing but this class modifies them
                //List<string> filepaths = new List<string>();
                //filepaths.Add(Path.Combine(NodeFolderPath, nodeid));
                //policy.ChangeMonitors.Add(new HostFileChangeMonitor(filepaths));

                policy.RemovedCallback = new CacheEntryRemovedCallback(OnRemoval);

                BPlusTreeNode node = BPlusTree.DeserializeNode(Path.Combine(NodeFolderPath, nodeid));

                NodeCache.Set(nodeid, node, policy);

                // TODO: Use something like the limit physical memory option on Memory cache itself
                // instead of this
                //if (NodeCache.GetCount() > 2000)
                //{
                //    NodeCache.Trim(10);
                //}

                return node;
            }
            else
            {
                return (BPlusTreeNode)ci_node.Value;
            }
        }

        public void AddNewNode(BPlusTreeNode node)
        {
            CacheItemPolicy policy = new CacheItemPolicy();

            policy.RemovedCallback = new CacheEntryRemovedCallback(OnRemoval);

            NodeCache.Set(node.NodeID, node, policy);
        }

        private void OnRemoval(CacheEntryRemovedArguments arguments)
        {
            BPlusTreeNode node = (BPlusTreeNode)arguments.CacheItem.Value;
            SaveIfDirty(node);
        }

        public void SynchronizeToDisk()
        {
            foreach (var cacheitem in NodeCache)
            {
                SaveIfDirty((BPlusTreeNode)cacheitem.Value);
            }
        }
        
        private void SaveIfDirty(BPlusTreeNode node)
        {
            // Save needed?
            if (node.Dirty)
            {
                node.Dirty = false;
                BPlusTree.SerializeNode(node, Path.Combine(NodeFolderPath, node.NodeID));
            }
        }
    }
}
