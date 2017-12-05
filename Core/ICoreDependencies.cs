using System;
using System.Collections.Generic;
using System.Text;
using System.IO;

namespace BackupCore
{
    public interface ICoreDependencies
    {
        ICoreSrcDependencies SrcDependencies { get; set; }

        ICoreDstDependencies DefaultDstDependencies { get; set; }

        ICoreDstDependencies CacheDependencies { get; set; }

        /// <summary>
        /// True if the regular backup destination is available.
        /// If the destination is not available we attempt to use the cache.
        /// </summary>
        bool DestinationAvailable { get; set; }

        void LoadDstAndCache();

        void InitializeNewDstAndCache(string bsname);
    }
}
