using System.IO;

namespace Apex.Serialization.Internal
{
    internal interface ISerializer
    {
        void Write<T>(T value, Stream outputStream);
        T Read<T>(Stream outputStream);
    }
}