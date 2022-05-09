namespace LagernCore.Utilities
{
    public interface ICustomDeserializableWithDependencies<T, D>
    {
        static abstract T Deserialize(byte[] data, D dependencies);
    }
}
