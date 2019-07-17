using System;
using System.IO;

namespace Apex.Serialization
{
    public interface IBinary : IDisposable
    {
        ImmutableSettings Settings { get; }

        void Intern(object o);
        void Precompile(Type type);
        void Precompile<T>();
        T Read<T>(Stream inputStream);
        void Write<T>(T value, Stream outputStream);

        void SetCustomHookContext<T>(T context)
            where T : class;
    }
}