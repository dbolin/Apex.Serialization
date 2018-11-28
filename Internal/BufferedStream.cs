using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Apex.Serialization.Internal
{
    internal sealed unsafe class BufferedStream : IBufferedStream, IDisposable
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

        private GCHandle _bufferGCHandle;
        private GCHandle _typeIdBufferGCHandle;

        private void* _bufferPtr;
        private byte* _typeIdBufferPtr;

        private uint _bufferPosition;
        private const uint MaxSize = 1024*1024;
        private int _size = (int)MaxSize;

        private DictionarySlim<Type, TypeIdCacheEntry> _typeIdCache = new DictionarySlim<Type, TypeIdCacheEntry>();

        // DEV fields
        private uint _reserved;

        internal BufferedStream()
        {
            _buffer = new byte[MaxSize];
            _typeIdBuffer = new byte[MaxSize * 4];
            PinBuffers();
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

        private void PinBuffers()
        {
            _bufferGCHandle = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _typeIdBufferGCHandle = GCHandle.Alloc(_typeIdBuffer, GCHandleType.Pinned);
            _bufferPtr = _bufferGCHandle.AddrOfPinnedObject().ToPointer();
            _typeIdBufferPtr = (byte*)_typeIdBufferGCHandle.AddrOfPinnedObject().ToPointer();
        }

        public void Flush()
        {
            if (_bufferPosition == 0)
            {
                return;
            }

            if (_writing)
            {
                _target.Write(_buffer, 0, (int)_bufferPosition);
                _bufferPosition = 0;
            }
            else
            {
                var len = (int)(_size - _bufferPosition);
                Unsafe.CopyBlock(_bufferPtr, Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), (uint)len);
                _size = _target.Read(_buffer, len, (int)_bufferPosition) + len;
                _bufferPosition = 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReserveSize(int sizeNeeded)
        {
            CheckSize();
            if (_size - _bufferPosition < sizeNeeded)
            {
                Flush();
            }

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


        public void Write(string input)
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
            uint sourcePosition = 0;
            fixed (void* text = input)
            {
                do
                {
                    var allowed = (uint)(_size - _bufferPosition);
                    if (byteCount > allowed)
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(text, (int)sourcePosition), allowed);
                        byteCount -= allowed;
                        _bufferPosition += allowed;
                        sourcePosition += allowed;
                        Flush();
                    }
                    else
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), Unsafe.Add<byte>(text, (int)sourcePosition), byteCount);
                        _bufferPosition += byteCount;
                        byteCount = 0;
                    }
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

            var name = type.FullName;
            var qualifiedInfo = type.Assembly.FullName;
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

        public string Read()
        {
            ReserveSize(4);
            var byteCount = Read<uint>();
            if (byteCount == uint.MaxValue)
            {
                return null;
            }

            byteCount = byteCount * 2;

            uint sourcePosition = 0;
            var result = new string('\0', (int)byteCount / 2);
            fixed (void* text = result)
            {
                do
                {
                    var allowed = (uint)(_size - _bufferPosition);
                    if (byteCount > allowed)
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(text, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), allowed);
                        byteCount -= allowed;
                        sourcePosition += allowed;
                        _bufferPosition += allowed;
                        Flush();
                    }
                    else
                    {
                        Unsafe.CopyBlock(Unsafe.Add<byte>(text, (int)sourcePosition), Unsafe.Add<byte>(_bufferPtr, (int)_bufferPosition), byteCount);
                        _bufferPosition += byteCount;
                        byteCount = 0;
                    }
                } while (byteCount > 0);
            }
            return result;
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
            Unsafe.CopyBlock((void*)_typeIdBufferPtr, (void*)typeId, (uint)(typeLen1 + typeLen2 + 2));

            ReorderBytes(typeId, typeLen1);
            ReorderBytes(typeId + typeLen1 + 2, typeLen2);

            var typeName = Encoding.UTF8.GetString(typeId, typeLen1 + typeLen2 + 2);

            typeId = _typeIdBufferPtr;

            _typeIdBufferPtr += typeLen1 + typeLen2 + 2;
            return Type.GetType(typeName, true);
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

        private bool disposedValue = false;

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                disposedValue = true;
                _bufferGCHandle.Free();
                _typeIdBufferGCHandle.Free();
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

    }
}
