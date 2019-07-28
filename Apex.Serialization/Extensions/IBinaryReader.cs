namespace Apex.Serialization.Extensions
{
    public interface IBinaryReader
    {
        string? Read();
        T Read<T>() where T : struct;

        T ReadObject<T>();
    }
}
