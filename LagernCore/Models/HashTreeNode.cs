using BackupCore.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace BackupCore.Models
{
    public class HashTreeNode
    {
        public HashTreeNode(List<(byte[] nodehash, HashTreeNode? node)> children)
        {
            Children = children;
        }

        List<(byte[] nodehash, HashTreeNode? node)> Children { get; set; }

        public ISkippableChildrenIterator<byte[]> GetChildIterator()
        {
            return new HashTreeNodeSkippableChildrenIterator(this);
            
        }

        public class HashTreeNodeSkippableChildrenIterator : ISkippableChildrenIterator<byte[]>
        {
            private HashTreeNodeSkippableChildrenIterator? ChildIterator { get; set; }

            private bool SkipChild { get; set; } = false;

            HashTreeNode Node { get; set; }

            public HashTreeNodeSkippableChildrenIterator(HashTreeNode node)
            {
                Node = node;
            }

            public IEnumerator<byte[]> GetEnumerator()
            {
                foreach (var (nodehash, node) in Node.Children)
                {
                    yield return nodehash;
                    if (node != null)
                    {
                        if (!SkipChild)
                        {
                            ChildIterator = new HashTreeNodeSkippableChildrenIterator(node);
                            foreach (var hash in ChildIterator)
                            {
                                yield return hash;
                            }
                            ChildIterator = null;
                        }
                        SkipChild = false;
                    }
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public void SkipChildrenOfCurrent()
            {
                if (ChildIterator != null)
                {
                    ChildIterator.SkipChildrenOfCurrent();
                }
                else
                {
                    SkipChild = true;
                }
            }
        }
    }
}
