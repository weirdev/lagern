using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BackupCore
{
    public interface ICustomSerializable<T>
    {
        byte[] serialize();

        // c# does not alllow static class methods to implement 
        // interface methods. And deserialize is class specific,
        // so a static interface method will not work. Therefore,
        // we will take it on faith that classes using serialize()
        // will also provide a static deserialize().
        //static T deserialize(byte[] data);
    }
}
