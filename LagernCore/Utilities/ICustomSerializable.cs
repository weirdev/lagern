﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LagernCore.Utilities
{
    public interface ICustomSerializable
    {
        byte[] Serialize();
    }
}
