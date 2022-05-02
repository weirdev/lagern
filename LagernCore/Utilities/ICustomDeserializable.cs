using LagernCore.Utilities;

namespace BackupCore
{
    public interface ICustomDeserializable<T>
    {
        static abstract T Deserialize(byte[] data);
    }
}
