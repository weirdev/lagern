using System;
using System.Collections.Generic;
using System.Text;

namespace BackupCore.Utilities
{
    public interface ISkippableChildrenIterator<T> : IEnumerable<T>
    {
        void SkipChildren();
    }
}
