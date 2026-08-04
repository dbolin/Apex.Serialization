using System;
using System.IO;

namespace Apex.Serialization
{
    public interface IBinary : IDisposable
    {
        void Intern(object o);
        void Precompile(Type type);
        void Precompile<T>();
        T Read<T>(Stream inputStream);
        void Write<T>(T value, Stream outputStream);

        void SetCustomHookContext<T>(T context)
            where T : class;

        /// <summary>
        /// Registers the instance that every occurrence of a boundary type will resolve to on the next
        /// Read.  There is one substitute per marked type, so all occurrences of it in a payload alias
        /// that instance.  Substitutes are cleared after each Read, so they must be registered per read
        /// operation.  Only supported for Graph serialization.
        /// <para>
        /// This method verifies only that the substitute is assignable to the marked type.  It must also
        /// be assignable to every concrete type the payload holds under that marked type, which cannot be
        /// checked until the payload is read; a mismatch throws an InvalidOperationException naming the
        /// types involved during the Read.  A payload containing two sibling subclasses of one marked
        /// base therefore cannot be satisfied, since no single instance is assignable to both.
        /// </para>
        /// </summary>
        /// <param name="type">The marked boundary type, or any subclass of one.</param>
        /// <param name="substitute">The instance to resolve occurrences of that type to.</param>
        void SetBoundarySubstitute(Type type, object substitute);

        /// <summary>
        /// Registers the instance that every occurrence of a boundary type will resolve to on the next
        /// Read.  There is one substitute per marked type, so all occurrences of it in a payload alias
        /// that instance.  Substitutes are cleared after each Read, so they must be registered per read
        /// operation.  Only supported for Graph serialization.
        /// <para>
        /// This method verifies only that the substitute is assignable to the marked type.  It must also
        /// be assignable to every concrete type the payload holds under that marked type, which cannot be
        /// checked until the payload is read; a mismatch throws an InvalidOperationException naming the
        /// types involved during the Read.  A payload containing two sibling subclasses of one marked
        /// base therefore cannot be satisfied, since no single instance is assignable to both.
        /// </para>
        /// </summary>
        /// <typeparam name="T">The marked boundary type, or any subclass of one.  Resolved through the
        /// class hierarchy, so registering with a subclass-typed variable still keys the marked base.</typeparam>
        /// <param name="substitute">The instance to resolve occurrences of that type to.</param>
        void SetBoundarySubstitute<T>(T substitute)
            where T : class;
    }
}