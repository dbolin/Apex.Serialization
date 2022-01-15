using Apex.Serialization.Internal;
using BenchmarkDotNet.Attributes;
using System;
using System.Collections.Generic;
using System.IO;
using FastExpressionCompiler.LightExpression;
using BufferedStream = Apex.Serialization.Internal.BufferedStream;

namespace Benchmark
{
    public class BufferedStreamPerformance
    {
        private BufferedStream stream = BufferedStream.Create();
        private MemoryStream m = new MemoryStream();

        private delegate void writeSig(ref BufferedStream stream, byte b);
        private writeSig writeByteMethod = CreateWriteByteMethod();

        private static writeSig CreateWriteByteMethod()
        {
            var p = Expression.Parameter(typeof(BufferedStream).MakeByRefType(), "stream");
            var b = Expression.Parameter(typeof(byte), "b");
            var lambda = Expression.Lambda<writeSig>(Expression.Call(p, BinaryStreamMethods<BufferedStream>.GenericMethods<byte>.WriteValueMethodInfo, b), p, b);
            var compiledLambda = lambda.CompileFast();
            return (writeSig)compiledLambda;
        }

        [Benchmark]
        public void WriteByte()
        {
            stream.WriteTo(m);

            for(int i=0;i<1000000;++i)
            {
                writeByteMethod(ref stream, 1);
            }
        }
    }
}
