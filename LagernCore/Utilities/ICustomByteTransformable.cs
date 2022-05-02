using LagernCore.Utilities;

namespace BackupCore
{
    public interface ICustomByteTransformable<T> : ICustomSerializable, ICustomDeserializable<T> { }
}
