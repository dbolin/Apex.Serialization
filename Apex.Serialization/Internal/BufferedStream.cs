using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Apex.Serialization.Internal
{
    internal unsafe struct BufferedStream : IBinaryStream, IDisposable
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
        private byte[] _typeIdBuffer;
        private List<byte[]> _previousTypeIdBuffers;
        private int _typeIdLengthRemaining;

        private GCHandle _bufferGCHandle;
        private List<GCHandle> _typeIdBufferGCHandles;

        private void* _bufferPtr;
        private byte* _typeIdBufferPtr;

        private uint _bufferPosition;
        private const uint MaxSize = 1024*1024;
        private int _size;

        private DictionarySlim<Type, TypeIdCacheEntry> _typeIdCache;

        // DEV fields
        private uint _reserved;

        internal static BufferedStream Create()
        {
            var stream = new BufferedStream();
            stream._size = (int)MaxSize;
            stream._previousTypeIdBuffers = new List<byte[]>();
            stream._typeIdBufferGCHandles = new List<GCHandle>();
            stream._typeIdCache = new DictionarySlim<Type, TypeIdCacheEntry>();
            stream._buffer = Binary.ByteArrayPool.Rent((int)MaxSize);
            stream._bufferGCHandle = GCHandle.Alloc(stream._buffer, GCHandleType.Pinned);
            stream._bufferPtr = stream._bufferGCHandle.AddrOfPinnedObject().ToPointer();
            stream.CreateNewTypeIdBuffer();
            return stream;
        }

        private void CreateNewTypeIdBuffer()
        {
            if(_typeIdBuffer != null)
            {
                _previousTypeIdBuffers.Add(_typeIdBuffer);
            }

            _typeIdBuffer = Binary.ByteArrayPool.Rent((int)MaxSize);
            _typeIdLengthRemaining = (int)MaxSize;
            var typeIdBufferGCHandle = GCHandle.Alloc(_typeIdBuffer, GCHandleType.Pinned);
            _typeIdBufferPtr = (byte*)typeIdBufferGCHandle.AddrOfPinnedObject().ToPointer();
            _typeIdBufferGCHandles.Add(typeIdBufferGCHandle);
        }

        public void ReadFrom(Stream stream)
        {
            if (_target != stream)
            {
                _target = stream;
            }

            _size = _target.Read(_buffer, 0, (int)MaxSize);
            _bufferPosition = 0;
            _writing = false;
        }

        public void WriteTo(Stream stream)
        {
            if (_target != stream)
            {
                _target = stream;
            }

            _size = (int)MaxSize;
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

                _target.Write(_buffer, 0, (int)_bufferPosition);
                _bufferPosition = 0;
                return true;
            }
            else
            {
                var len = (int)(_size - _bufferPosition);
                Unsafe.CopyBlock(_bufferPtr, Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), (uint)len);
                _size = _target.Read(_buffer, len, (int)MaxSize - len);
                var result = _size != 0;
                _size += len;
                _bufferPosition = 0;
                return result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReserveSize(int sizeNeeded)
        {
            CheckSize();
            while (_size - _bufferPosition < sizeNeeded && Flush())
                ;

            SetReserved(sizeNeeded);
        }

        [Conditional("DEV")]
        private void CheckSize()
        {
            if (_size == 0)
            {
                throw new InvalidOperationException();
            }
        }

        [Conditional("DEV")]
        private void SetReserved(int size)
        {
            _reserved = (uint)(_bufferPosition + size);
        }

        [Conditional("DEV")]
        private void CheckReserved(int size)
        {
            if(_bufferPosition + size > _reserved)
            {
                throw new InvalidOperationException();
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

            var byteCount = (uint)input.Length;
            Write(byteCount);
            byteCount *= 2;

            var allowed = (uint)(_size - _bufferPosition);
            if (input.Length < 2048 && allowed >= byteCount)
            {
                fixed (void* text = input)
                {
                    Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), text, byteCount);
                    _bufferPosition += byteCount;
                    return;
                }
            }

            WriteStringSlow(input, byteCount, allowed);
        }

        private void WriteStringSlow(string input, uint byteCount, uint allowed)
        {
#if NETCOREAPP3_0
            if (input.Length >= 2048)
            {
                Flush();
                fixed (void* s = input)
                {
                    _target.Write(new ReadOnlySpan<byte>(s, (int)byteCount));
                    return;
                }
            }
#endif

            uint sourcePosition = 0;
            fixed (void* text = input)
            {
                do
                {
                    if (byteCount > allowed)
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(text, (int)sourcePosition), allowed);
                        byteCount -= allowed;
                        _bufferPosition += allowed;
                        sourcePosition += allowed;
                        if (!Flush())
                        {
                            ThrowFailedStream();
                        }
                    }
                    else
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(text, (int)sourcePosition), byteCount);
                        _bufferPosition += byteCount;
                        byteCount = 0;
                    }
                    allowed = (uint)(_size - _bufferPosition);
                } while (byteCount > 0);
            }
        }

        public void WriteTypeId(Type type)
        {
            ref var entry = ref _typeIdCache.GetOrAddValueRef(type);

            if (entry.Bytes != null)
            {
                fixed (byte* b = &entry.Bytes[0])
                {
                    ReserveSize(10 + entry.Length1 + entry.Length2);
                    Write(entry.Length1);
                    Write(entry.Length2);
                    WriteBytes(b, entry.Length1 + entry.Length2 + 2);
                    return;
                }
            }

            var name = type.FullName!;
            var qualifiedInfo = type.Assembly.FullName!;
            var infoBytes = new byte[(name.Length + qualifiedInfo.Length) * Encoding.UTF8.GetMaxByteCount(1)];

            fixed (char* namePtr = name, asmPtr = qualifiedInfo)
            {
                fixed (byte* stageBytes = infoBytes)
                {
                    var stage = stageBytes;
                    var start = stage;
                    var nameLen = Encoding.UTF8.GetBytes(namePtr, name.Length, stage, (int) MaxSize);
                    ReorderBytes(stage, nameLen);

                    stage += nameLen;
                    *stage = (byte) ',';
                    stage++;
                    *stage = (byte) ' ';
                    stage++;

                    var asmLen = Encoding.UTF8.GetBytes(asmPtr, qualifiedInfo.Length, stage, (int) MaxSize);
                    ReorderBytes(stage, asmLen);
                    stage += asmLen;

                    var totalSize = nameLen + asmLen + 2;
                    ReserveSize(totalSize + 8);
                    Write(nameLen);
                    Write(asmLen);
                    WriteBytes(start, totalSize);
                    entry = new TypeIdCacheEntry {Bytes = infoBytes, Length1 = nameLen, Length2 = asmLen};
                }
            }
        }

        private void WriteBytes(byte* start, int totalSize)
        {
            CheckReserved(totalSize);

            Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), (void*) start, (uint) totalSize);
            _bufferPosition += (uint)totalSize;
        }

        private void ReorderBytes(byte* typeIdBufferPtr, int length)
        {
            for (int i = 0; i < length; i+=2, length-=2)
            {
                var t = typeIdBufferPtr[i];
                typeIdBufferPtr[i] = typeIdBufferPtr[length - 1];
                typeIdBufferPtr[length - 1] = t;
            }
        }

        public string? Read()
        {
            ReserveSize(4);
            var byteCount = Read<uint>();
            if (byteCount == uint.MaxValue)
            {
                return null;
            }

            byteCount *= 2;

            var result = new string('\0', (int)byteCount / 2);
            var allowed = (uint)(_size - _bufferPosition);

            if (allowed >= byteCount)
            {
                fixed (void* text = result)
                {
                    Unsafe.CopyBlock(text, Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), byteCount);
                    _bufferPosition += byteCount;
                    return result;
                }
            }

            ReadStringSlow(byteCount, result, allowed);
            return result;
        }

        private void ReadStringSlow(uint byteCount, string result, uint allowed)
        {
            uint sourcePosition = 0;
            fixed (void* text = result)
            {
                do
                {
                    if (byteCount > allowed)
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(text, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), allowed);
                        byteCount -= allowed;
                        sourcePosition += allowed;
                        _bufferPosition += allowed;
                        if (!Flush())
                        {
                            ThrowFailedStream();
                        }
                    }
                    else
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(text, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), byteCount);
                        _bufferPosition += byteCount;
                        byteCount = 0;
                    }
                    allowed = (uint)(_size - _bufferPosition);
                } while (byteCount > 0);
            }
        }

        public byte* ReadTypeId(out int length1, out int length2)
        {
            ReserveSize(8);
            length1 = Read<int>();
            length2 = Read<int>();
            var totalLength = length1 + length2 + 2;
            ReserveSize(totalLength);
            var result = Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition);
            _bufferPosition += (uint)totalLength;
            return (byte*)result;
        }

        public Type RestoreTypeFromId(ref byte* typeId, int typeLen1, int typeLen2)
        {
            if(_typeIdLengthRemaining < typeLen1 + typeLen2 + 2)
            {
                CreateNewTypeIdBuffer();
            }

            Unsafe.CopyBlock((void*)_typeIdBufferPtr, (void*)typeId, (uint)(typeLen1 + typeLen2 + 2));

            ReorderBytes(typeId, typeLen1);
            ReorderBytes(typeId + typeLen1 + 2, typeLen2);

            var typeName = Encoding.UTF8.GetString(typeId, typeLen1 + typeLen2 + 2);

            typeId = _typeIdBufferPtr;

            var totalLen = typeLen1 + typeLen2 + 2;
            _typeIdBufferPtr += totalLen;
            _typeIdLengthRemaining -= totalLen;
            return Type.GetType(typeName, true)!;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<T>(T value) where T : struct
        {
            CheckReserved(Unsafe.SizeOf<T>());

            Unsafe.Write(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), value);
            _bufferPosition += (uint)Unsafe.SizeOf<T>();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read<T>() where T : struct
        {
            CheckReserved(Unsafe.SizeOf<T>());

            var res = Unsafe.Read<T>(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition));
            _bufferPosition += (uint)Unsafe.SizeOf<T>();
            return res;
        }

        private bool disposedValue;

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
                _bufferGCHandle.Free();
                foreach(var handle in _typeIdBufferGCHandles)
                {
                    handle.Free();
                }

                foreach(var buf in _previousTypeIdBuffers)
                {
                    Binary.ByteArrayPool.Return(buf);
                }

                Binary.ByteArrayPool.Return(_typeIdBuffer);
                Binary.ByteArrayPool.Return(_buffer);
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void WriteBytes(void* source, uint length)
        {
            var allowed = (uint)(_size - _bufferPosition);

            if (length < 4096 && allowed >= length)
            {
                Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), source, length);
                _bufferPosition += length;
                return;
            }

            WriteBytesSlow(source, length, allowed);
        }

        private void WriteBytesSlow(void* source, uint length, uint allowed)
        {
#if NETCOREAPP3_0
            if (length >= 4096)
            {
                Flush();
                _target.Write(new ReadOnlySpan<byte>(source, (int)length));
                return;
            }
#endif
            uint sourcePosition = 0;
            do
            {
                if (length > allowed)
                {
                    Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(source, (int)sourcePosition), allowed);
                    length -= allowed;
                    _bufferPosition += allowed;
                    sourcePosition += allowed;
                    if (!Flush())
                    {
                        ThrowFailedStream();
                    }
                }
                else
                {
                    Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(source, (int)sourcePosition), length);
                    _bufferPosition += length;
                    length = 0;
                }
                allowed = (uint)(_size - _bufferPosition);
            } while (length > 0);
        }

        public void ReadBytes(void* destination, uint length)
        {
            var allowed = (uint)(_size - _bufferPosition);

            if (allowed >= length)
            {
                Unsafe.CopyBlock(destination, Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), length);
                _bufferPosition += length;
                return;
            }

            ReadBytesSlow(destination, length, allowed);
        }

        private void ReadBytesSlow(void* destination, uint length, uint allowed)
        {
            uint sourcePosition = 0;
            do
            {
                if (length > allowed)
                {
                    Unsafe.CopyBlock(Unsafe.Add<byte>(destination, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), allowed);
                    length -= allowed;
                    sourcePosition += allowed;
                    _bufferPosition += allowed;
                    if (!Flush())
                    {
                        ThrowFailedStream();
                    }
                }
                else
                {
                    Unsafe.CopyBlock(Unsafe.Add<byte>(destination, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), length);
                    _bufferPosition += length;
                    length = 0;
                }
                allowed = (uint)(_size - _bufferPosition);
            } while (length > 0);
        }

        private void ThrowFailedStream()
        {
            throw new InvalidOperationException("Underlying stream failed to read or write");
        }
    }
}
