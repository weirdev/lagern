using System.Collections.Generic;

namespace BackupCore.Utilities
{
    public interface ISkippableChildrenEnumerable<T> : IEnumerable<T>
    {
        void SkipChildrenOfCurrent();
    }
}
