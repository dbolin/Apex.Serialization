using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.Loader;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// Resolves a serialized assembly-qualified type name among the assemblies already loaded.  When none matches, it loads
    /// the named assembly only if a file with that simple name is in the runtime's trusted platform assemblies list (the
    /// framework and the application's own dependencies), so a stored name can't make the reader load any other file.
    /// A type the assembly forwards is followed to the assembly the forwarder names, which the runtime binds as it binds
    /// that assembly's other references: some of the framework's facades forward to assemblies outside the list (mscorlib
    /// to System.Security.Permissions, for one), which only the application's assembly resolve handlers can then supply.
    /// <para>
    /// A name is cached only when it is exactly what <see cref="BufferedStream.WriteTypeId"/> writes for the type it
    /// resolves to, which keeps the cache to one entry per type however many spellings resolve.
    /// </para>
    /// </summary>
    internal static class TypeResolver
    {
        // A Func over three four-element ValueTuples and an int already passes the parser's default of 20 nodes; the limit
        // only has to keep a hostile name from recursing deeply in the parser and in Resolve.
        private static readonly TypeNameParseOptions ParseOptions = new TypeNameParseOptions { MaxNodes = 128 };

        private static readonly ConcurrentDictionary<string, Type> _canonicalNames = new ConcurrentDictionary<string, Type>();

        private static readonly Lazy<HashSet<string>> _trustedPlatformAssemblies = new Lazy<HashSet<string>>(ReadTrustedPlatformAssemblies);

        internal static Type? Find(string assemblyQualifiedName, out bool isCanonical)
        {
            if (_canonicalNames.TryGetValue(assemblyQualifiedName, out var known))
            {
                isCanonical = true;
                return known;
            }

            var type = TypeName.TryParse(assemblyQualifiedName, out var name, ParseOptions) ? TryResolve(name) : null;
            isCanonical = type != null && CanonicalName(type) == assemblyQualifiedName;
            if (isCanonical)
            {
                _canonicalNames.TryAdd(assemblyQualifiedName, type!);
            }

            return type;
        }

        internal static string CanonicalName(Type type) => type.FullName + ", " + type.Assembly.FullName;

        /// <summary>
        /// Returns null for a type the runtime can't create, such as an array of byrefs or a generic type over arguments
        /// that break its constraints, and for a type whose assembly fails to load.
        /// </summary>
        private static Type? TryResolve(TypeName name)
        {
            try
            {
                return Resolve(name);
            }
            catch (Exception e) when (e is ArgumentException or TypeLoadException or IOException or BadImageFormatException)
            {
                return null;
            }
        }

        private static Type? Resolve(TypeName name)
        {
            if (name.IsSimple)
            {
                return name.AssemblyName == null ? null : FindAssembly(name.AssemblyName)?.GetType(name.FullName, throwOnError: false, ignoreCase: false);
            }

            if (name.IsConstructedGenericType)
            {
                var definition = Resolve(name.GetGenericTypeDefinition());
                if (definition == null || !definition.IsGenericTypeDefinition)
                {
                    return null;
                }

                var arguments = name.GetGenericArguments();
                var resolved = new Type[arguments.Length];
                for (var i = 0; i < arguments.Length; ++i)
                {
                    var argument = Resolve(arguments[i]);
                    if (argument == null)
                    {
                        return null;
                    }

                    resolved[i] = argument;
                }

                return definition.MakeGenericType(resolved);
            }

            var element = Resolve(name.GetElementType());
            return element == null ? null
                : name.IsSZArray ? element.MakeArrayType()
                : name.IsArray ? element.MakeArrayType(name.GetArrayRank())
                : name.IsByRef ? element.MakeByRefType()
                : element.MakePointerType();
        }

        private static Assembly? FindAssembly(AssemblyNameInfo name) => FindLoadedAssembly(name) ?? LoadTrustedPlatformAssembly(name.Name);

        /// <summary>
        /// Prefers the assembly whose full name matches; otherwise takes one with the same simple name, so data written
        /// against another version of the assembly still reads.
        /// </summary>
        private static Assembly? FindLoadedAssembly(AssemblyNameInfo name)
        {
            Assembly? sameSimpleName = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.FullName == name.FullName)
                {
                    return assembly;
                }

                if (sameSimpleName == null && string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                {
                    sameSimpleName = assembly;
                }
            }

            return sameSimpleName;
        }

        private static Assembly? LoadTrustedPlatformAssembly(string simpleName)
        {
            // Loading by the stored full name fails when the stored version isn't the file's.
            return _trustedPlatformAssemblies.Value.Contains(simpleName)
                ? AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName { Name = simpleName })
                : null;
        }

        private static HashSet<string> ReadTrustedPlatformAssemblies()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string paths)
            {
                foreach (var path in paths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    names.Add(Path.GetFileNameWithoutExtension(path));
                }
            }

            return names;
        }
    }
}
