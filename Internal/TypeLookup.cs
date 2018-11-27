using System;

namespace Apex.Serialization.Internal
{
    internal sealed unsafe class TypeLookup<T>
    {
        private Node<T> _root;

        public TypeLookup()
        {
            _root = new Node<T>();
        }

        public T Find(byte* key, int length)
        {
            ref var current = ref _root;
            while (true)
            {
                if (current.Children == null)
                {
                    var span1 = new Span<byte>(current.Key, current.KeyLength);
                    var span2 = new Span<byte>(key, length);
                    if (span1.SequenceEqual(span2))
                    {
                        return current.Value;
                    }

                    return default;
                }

                current = ref current.Children[*key];
                key++;
                length--;
            }
        }

        public void Add(byte* key, int length, T value)
        {
            Add(ref _root, key, length, value);
        }

        private void Add(ref Node<T> node, byte* key, int length, T value)
        {
            if (node.Children == null)
            {
                if (node.Key == null)
                {
                    node.Key = key;
                    node.KeyLength = length;
                    node.Value = value;
                    return;
                }

                node.Children = new Node<T>[256];
                node.Children[*node.Key] = new Node<T> {Key = node.Key + 1, KeyLength = node.KeyLength - 1, Value = node.Value};
            }

            Add(ref node.Children[*key], key + 1, length - 1, value);
        }
    }

    internal unsafe struct Node<T>
    {
        public byte* Key;
        public int KeyLength;
        public T Value;

        public Node<T>[] Children;
    }
}
