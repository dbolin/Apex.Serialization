using System.Runtime.CompilerServices;

namespace Apex.Serialization.Extensions
{
    internal class BinaryReader : IBinaryReader
    {
        public Binary _instance;

        public BinaryReader(Binary instance)
        {
            _instance = instance;
        }

        public string Read()
        {
            return _instance._stream.Read();
        }

        public T Read<T>() where T : struct
        {
            _instance._stream.ReserveSize(Unsafe.SizeOf<T>());
            return _instance._stream.Read<T>();
        }

        public T ReadObject<T>()
        {
            return _instance.ReadObjectEntry<T>();
        }
    }
}
