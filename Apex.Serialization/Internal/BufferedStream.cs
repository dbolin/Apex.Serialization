using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Apex.Serialization.Internal
{
    /// <summary>
    /// Stages reads and writes in one pooled buffer of <see cref="MaxSize"/> bytes.
    /// <para>
    /// A reservation is the most a value can take, not what it takes (a null reference consumes one byte of the nine its
    /// header reserves, and a struct with a custom read may consume none), so a stream may end inside one or before it:
    /// reading then zeroes the part of the reservation the stream didn't supply, and a value read into that part leaves
    /// the position past <see cref="_size"/>, which the next reservation, refill or <see cref="ThrowIfReadPastEnd"/>
    /// reports.  A value that always takes all its bytes and is checked as soon as it's read (a type reference, a
    /// version id) calls <see cref="ThrowIfStreamEndsWithin"/> first, or the check reports the zeroes as invalid data.
    /// </para>
    /// <para>
    /// A length is checked against what a stream that can seek has left, and against a stream that can't seek by
    /// buffering the bytes it implies when they fit the buffer (<see cref="TryEnsureAvailable"/>).  A longer blittable
    /// array or string from a stream that can't seek is read into an allocation grown as the bytes arrive, so the buffer
    /// stays at <see cref="MaxSize"/> for it.  Only checking the element count of another array reads further ahead
    /// (<see cref="ReadAhead"/>), growing the buffer only once the stream has filled it, until
    /// <see cref="ReleaseReadAhead"/>.
    /// </para>
    /// </summary>
    internal struct BufferedStream : IBinaryStream, IDisposable
    {
        private struct TypeIdCacheEntry
        {
            public byte[] Bytes;
            public int Length1;
            public int Length2;
        }

        private Stream _target;
        private bool _writing;

        private byte[] _buffer;
        private byte[] _pooledBuffer;

        private int _bufferPosition;
        internal const int MaxSize = 1024*1024;
        private int _size;

        private DictionarySlim<Type, TypeIdCacheEntry> _typeIdCache;

        // DEV fields
        private int _reserved;

        internal static BufferedStream Create()
        {
            var stream = new BufferedStream();
            stream._size = MaxSize;
            stream._typeIdCache = new DictionarySlim<Type, TypeIdCacheEntry>();
            stream._pooledBuffer = Binary.ByteArrayPool.Rent(MaxSize);
            stream._buffer = stream._pooledBuffer;
            return stream;
        }

        public void ReadFrom(Stream stream)
        {
            if (_target != stream)
            {
                _target = stream;
            }

            ReleaseReadAhead();
            _size = _target.Read(_buffer, 0, MaxSize);
            _bufferPosition = 0;
            _writing = false;
        }

        public void WriteTo(Stream stream)
        {
            if (_target != stream)
            {
                _target = stream;
            }

            ReleaseReadAhead();
            _size = MaxSize;
            _bufferPosition = 0;
            _writing = true;
        }

        public bool Flush()
        {
            if (_writing)
            {
                if (_bufferPosition == 0)
                {
                    return true;
                }

                _target.Write(_buffer, 0, _bufferPosition);
                _bufferPosition = 0;
                return true;
            }
            else
            {
                var len = _size - _bufferPosition;
                if (len < 0)
                {
                    ThrowHelper.ThrowEndOfStream();
                }

                if (_bufferPosition != 0)
                {
                    _buffer.AsSpan(_bufferPosition, len).CopyTo(_buffer);
                    _bufferPosition = 0;
                }

                var read = _target.Read(_buffer, len, _buffer.Length - len);
                _size = len + read;
                return read != 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReserveSize(int sizeNeeded)
        {
            if (_size - _bufferPosition < sizeNeeded)
            {
                ReserveSizeSlow(sizeNeeded);
            }

            SetReserved(sizeNeeded);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReserveSizeSlow(int sizeNeeded)
        {
            if ((uint)sizeNeeded > MaxSize)
            {
                ThrowHelper.ThrowReservationTooLarge(sizeNeeded);
            }

            while (_size - _bufferPosition < sizeNeeded)
            {
                if (!Flush())
                {
                    _buffer.AsSpan(_size, sizeNeeded - _size).Clear();
                    return;
                }
            }
        }

        public void ReadAhead(int byteCount)
        {
            while (_size - _bufferPosition < byteCount)
            {
                if (_bufferPosition == 0 && _size == _buffer.Length)
                {
                    var grown = new byte[(int)Math.Min(2L * _buffer.Length, Array.MaxLength)];
                    _buffer.AsSpan(0, _size).CopyTo(grown);
                    _buffer = grown;
                }

                if (!Flush())
                {
                    ThrowHelper.ThrowEndOfStream();
                }
            }
        }

        /// <summary>
        /// Skips the store when the buffer is already the pooled one, since storing a reference field pays the GC write
        /// barrier.
        /// </summary>
        public void ReleaseReadAhead()
        {
            if (_buffer != _pooledBuffer)
            {
                _buffer = _pooledBuffer;
            }
        }

        public bool TryEnsureAvailable(long byteCount)
        {
            var buffered = _size - _bufferPosition;
            if (byteCount <= buffered)
            {
                return true;
            }

            if (_target.CanSeek)
            {
                ThrowIfSeekableStreamLacks(byteCount - buffered);
                return true;
            }

            if (byteCount > _buffer.Length)
            {
                return false;
            }

            ReadAhead((int)byteCount);
            return true;
        }

        private void ThrowIfSeekableStreamLacks(long unbufferedByteCount)
        {
            if (unbufferedByteCount > _target.Length - _target.Position)
            {
                ThrowHelper.ThrowEndOfStream();
            }
        }

        public void ThrowIfReadPastEnd()
        {
            if (_bufferPosition > _size)
            {
                ThrowHelper.ThrowEndOfStream();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ThrowIfStreamEndsWithin(int byteCount)
        {
            CheckReserved(byteCount);
            if (_size - _bufferPosition < byteCount)
            {
                ThrowHelper.ThrowEndOfStream();
            }
        }

        [Conditional("DEV")]
        private void SetReserved(int size)
        {
            _reserved = _bufferPosition + size;
        }

        internal const string ReservationOverrun = "A read or write went past its reservation";

        [Conditional("DEV")]
        private void CheckReserved(int size)
        {
            if(_bufferPosition + size > _reserved)
            {
                throw new InvalidOperationException(ReservationOverrun);
            }
        }

        public void Write(string? input)
        {
            ReserveSize(40);
            if (input == null)
            {
                Write(uint.MaxValue);
                return;
            }

            Write((uint)input.Length);
            WriteBytes(MemoryMarshal.AsBytes(input.AsSpan()));
        }

        public void WriteTypeId(Type type)
        {
            ref var entry = ref _typeIdCache.GetOrAddValueRef(type);

            if (entry.Bytes == null)
            {
                var name = type.FullName!;
                var qualifiedInfo = type.Assembly.FullName!;
                var infoBytes = new byte[Encoding.UTF8.GetMaxByteCount(name.Length) + 2 + Encoding.UTF8.GetMaxByteCount(qualifiedInfo.Length)];

                var nameLen = Encoding.UTF8.GetBytes(name, infoBytes);
                ReorderBytes(infoBytes.AsSpan(0, nameLen));
                infoBytes[nameLen] = (byte)',';
                infoBytes[nameLen + 1] = (byte)' ';

                var asmLen = Encoding.UTF8.GetBytes(qualifiedInfo, infoBytes.AsSpan(nameLen + 2));
                ReorderBytes(infoBytes.AsSpan(nameLen + 2, asmLen));

                entry = new TypeIdCacheEntry {Bytes = infoBytes, Length1 = nameLen, Length2 = asmLen};
            }

            var totalSize = entry.Length1 + entry.Length2 + 2;
            ReserveSize(totalSize + 8);
            Write(entry.Length1);
            Write(entry.Length2);
            CheckReserved(totalSize);
            entry.Bytes.AsSpan(0, totalSize).CopyTo(_buffer.AsSpan(_bufferPosition));
            _bufferPosition += totalSize;
        }

        private static void ReorderBytes(Span<byte> typeIdBytes)
        {
            for (int i = 0, length = typeIdBytes.Length; i < length; i+=2, length-=2)
            {
                var t = typeIdBytes[i];
                typeIdBytes[i] = typeIdBytes[length - 1];
                typeIdBytes[length - 1] = t;
            }
        }

        // The runtime's limit on a string's length, which it doesn't expose.
        private const int MaxStringLength = 0x3FFFFFDF;

        public string? Read()
        {
            ReserveSize(4);
            var length = Read<uint>();
            if (length == uint.MaxValue)
            {
                return null;
            }

            if (length > MaxStringLength)
            {
                ThrowHelper.ThrowInvalidLength("string length", length);
            }

            var byteCount = (int)length * 2;
            if (_size - _bufferPosition < byteCount)
            {
                if (!TryEnsureAvailable(byteCount))
                {
                    return ReadStringAsSupplied((int)length);
                }

                if (_size - _bufferPosition < byteCount)
                {
                    return ReadUnbufferedString(byteCount);
                }
            }

            var result = new string(MemoryMarshal.Cast<byte, char>(_buffer.AsSpan(_bufferPosition, byteCount)));
            _bufferPosition += byteCount;
            return result;
        }

        /// <summary>
        /// Reads a string longer than a stream that can't seek has supplied, doubling its characters from one buffer's
        /// worth only as they arrive, so what a corrupt length allocates stays proportional to what the stream holds.
        /// </summary>
        private string ReadStringAsSupplied(int length)
        {
            var chars = new char[Math.Min(length, MaxSize / sizeof(char))];
            var read = 0;
            while (true)
            {
                ReadBytes(MemoryMarshal.AsBytes(chars.AsSpan(read)));
                read = chars.Length;
                if (read == length)
                {
                    return new string(chars);
                }

                Array.Resize(ref chars, (int)Math.Min(length, 2L * read));
            }
        }

        /// <summary>
        /// Allocates the whole string before reading the rest of it straight from the stream, so call it only for a
        /// length a seekable stream was checked to hold.
        /// </summary>
        private string ReadUnbufferedString(int byteCount)
        {
            var buffered = _size - _bufferPosition;
            if (buffered < 0)
            {
                ThrowHelper.ThrowEndOfStream();
            }

            var copied = string.Create(byteCount / 2, (Source: _target, Buffer: _buffer, Start: _bufferPosition, Buffered: buffered),
                static (chars, state) =>
                {
                    var destination = MemoryMarshal.AsBytes(chars);
                    state.Buffer.AsSpan(state.Start, state.Buffered).CopyTo(destination);
                    state.Source.ReadExactly(destination.Slice(state.Buffered));
                });
            _bufferPosition = _size;
            return copied;
        }

        private const int TypeIdHeaderSize = 8;

        public ReadOnlySpan<byte> ReadTypeId()
        {
            ReserveSize(TypeIdHeaderSize);
            var length1 = MemoryMarshal.Read<int>(_buffer.AsSpan(_bufferPosition));
            var length2 = MemoryMarshal.Read<int>(_buffer.AsSpan(_bufferPosition + 4));
            var totalLength = TypeIdHeaderSize + (long)length1 + length2 + 2;
            if (length1 < 0 || length2 < 0 || totalLength > MaxSize)
            {
                ThrowHelper.ThrowInvalidLength("type id length", totalLength);
            }

            ReadAhead((int)totalLength);
            var result = new ReadOnlySpan<byte>(_buffer, _bufferPosition, (int)totalLength);
            _bufferPosition += (int)totalLength;
            return result;
        }

        public Type RestoreTypeFromId(ReadOnlySpan<byte> typeId, out bool isCanonical)
        {
            var length1 = MemoryMarshal.Read<int>(typeId);
            var name = typeId.Slice(TypeIdHeaderSize).ToArray();
            ReorderBytes(name.AsSpan(0, length1));
            ReorderBytes(name.AsSpan(length1 + 2));

            var assemblyQualifiedName = Encoding.UTF8.GetString(name);
            return TypeResolver.Find(assemblyQualifiedName, out isCanonical) ?? ThrowHelper.ThrowUnresolvableType(assemblyQualifiedName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<T>(T value) where T : struct
        {
            CheckReserved(Unsafe.SizeOf<T>());

            var buffer = _buffer;
            var position = _bufferPosition;
            if ((ulong)(uint)position + (uint)Unsafe.SizeOf<T>() > (uint)buffer.Length)
            {
                ThrowHelper.ThrowOutsideBuffer();
            }

            var destination = MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), (nint)(uint)position), Unsafe.SizeOf<T>());
            MemoryMarshal.Write(destination, in value);
            if (PaddingMap<T>.HasPadding)
            {
                PaddingMap<T>.ZeroPadding(destination);
            }

            _bufferPosition += Unsafe.SizeOf<T>();
        }

        /// <summary>
        /// Checks the bounds itself, as <see cref="Write{T}"/> does, rather than through the span constructor, which also
        /// tests the buffer for null on every call: generated code reads and writes every primitive field through these.
        /// The two don't share a helper: on .NET 10 the extra inlining level uses up the JIT's inline budget in large
        /// generated read methods, which then call this instead of inlining it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read<T>() where T : struct
        {
            CheckReserved(Unsafe.SizeOf<T>());

            var buffer = _buffer;
            var position = _bufferPosition;
            if ((ulong)(uint)position + (uint)Unsafe.SizeOf<T>() > (uint)buffer.Length)
            {
                ThrowHelper.ThrowOutsideBuffer();
            }

            var res = MemoryMarshal.Read<T>(MemoryMarshal.CreateReadOnlySpan(
                ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(buffer), (nint)(uint)position), Unsafe.SizeOf<T>()));
            _bufferPosition += Unsafe.SizeOf<T>();
            return res;
        }

        private bool disposedValue;

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
                Binary.ByteArrayPool.Return(_pooledBuffer);
                _pooledBuffer = null!;
                _buffer = null!;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void WriteValues<T>(ReadOnlySpan<byte> source) where T : struct
        {
            if (PaddingMap<T>.HasPadding)
            {
                WriteValuesZeroingPadding<T>(source);
            }
            else
            {
                WriteBytes(source);
            }
        }

        private void WriteValuesZeroingPadding<T>(ReadOnlySpan<byte> source) where T : struct
        {
            var written = 0;
            while (written < source.Length)
            {
                var allowed = _size - _bufferPosition;
                if (allowed == 0)
                {
                    Flush();
                    continue;
                }

                var count = Math.Min(allowed, source.Length - written);
                PaddingMap<T>.CopyZeroingPadding(_buffer.AsSpan(_bufferPosition, count), source.Slice(written, count), (uint)written);
                _bufferPosition += count;
                written += count;
            }
        }

        public void WriteBytes(ReadOnlySpan<byte> source)
        {
            if (source.Length < 4096 && source.Length <= _size - _bufferPosition)
            {
                source.CopyTo(_buffer.AsSpan(_bufferPosition));
                _bufferPosition += source.Length;
                return;
            }

            WriteBytesSlow(source);
        }

        private void WriteBytesSlow(ReadOnlySpan<byte> source)
        {
            if (source.Length >= 4096)
            {
                Flush();
                _target.Write(source);
                return;
            }

            while (source.Length > _size - _bufferPosition)
            {
                var allowed = _size - _bufferPosition;
                source.Slice(0, allowed).CopyTo(_buffer.AsSpan(_bufferPosition));
                _bufferPosition += allowed;
                source = source.Slice(allowed);
                Flush();
            }

            source.CopyTo(_buffer.AsSpan(_bufferPosition));
            _bufferPosition += source.Length;
        }

        public void ReadBytes(Span<byte> destination)
        {
            if (destination.Length <= _size - _bufferPosition)
            {
                _buffer.AsSpan(_bufferPosition, destination.Length).CopyTo(destination);
                _bufferPosition += destination.Length;
                return;
            }

            ReadBytesSlow(destination);
        }

        private void ReadBytesSlow(Span<byte> destination)
        {
            while (destination.Length > _size - _bufferPosition)
            {
                var allowed = _size - _bufferPosition;
                if (allowed > 0)
                {
                    _buffer.AsSpan(_bufferPosition, allowed).CopyTo(destination);
                    _bufferPosition += allowed;
                    destination = destination.Slice(allowed);
                }

                if (!Flush())
                {
                    ThrowHelper.ThrowEndOfStream();
                }
            }

            _buffer.AsSpan(_bufferPosition, destination.Length).CopyTo(destination);
            _bufferPosition += destination.Length;
        }
    }
}
