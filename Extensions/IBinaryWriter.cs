namespace Apex.Serialization.Extensions
{
    public interface IBinaryWriter
    {
        void Write(string input);
        void Write<T>(T value) where T : struct;

        void WriteObject<T>(T value);
    }
}
