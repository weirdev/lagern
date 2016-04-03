using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    interface IMemberFilter<T>
    {
        bool AddItem(T item);
        bool ContainsItem(T item);
        bool RemoveItem(T item);
    }
}
