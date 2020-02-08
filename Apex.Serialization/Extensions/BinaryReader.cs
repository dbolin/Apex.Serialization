using Apex.Serialization.Internal;
using System.Runtime.CompilerServices;

namespace Apex.Serialization.Extensions
{
    internal class BinaryReader<TStream, TSettingGen> : IBinaryReader
        where TStream : struct, IBinaryStream
    {
        public Binary<TStream, TSettingGen> _instance;

        public BinaryReader(Binary<TStream, TSettingGen> instance)
        {
            _instance = instance;
        }

        public string? Read()
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
