using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LagernCore.Utilities
{
    public interface ICustomDeserializableWithDependencies<T, D>
    {
        static abstract T Deserialize(byte[] data, D dependencies);
    }
}
