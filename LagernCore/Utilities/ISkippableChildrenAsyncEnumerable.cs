using System.Collections.Generic;

namespace BackupCore.Utilities
{
    public interface ISkippableChildrenAsyncEnumerable<T> : IAsyncEnumerable<T>
    {
        void SkipChildrenOfCurrent();
    }
}
