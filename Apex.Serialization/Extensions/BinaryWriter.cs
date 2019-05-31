using System.Runtime.CompilerServices;

namespace Apex.Serialization.Extensions
{
    internal class BinaryWriter : IBinaryWriter
    {
        public Binary _instance;

        public BinaryWriter(Binary instance)
        {
            _instance = instance;
        }

        public void Write(string input)
        {
            _instance._stream.Write(input);
        }

        public void Write<T>(T value) where T : struct 
        {
            _instance._stream.ReserveSize(Unsafe.SizeOf<T>());
            _instance._stream.Write(value);
        }

        public void WriteObject<T>(T value)
        {
            _instance.WriteObjectEntry(value);
        }
    }
}
