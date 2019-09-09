using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MartinCl2.Text.Json.Serialization.Compiler;

namespace MartinCl2.Text.Json.Serialization
{
    public abstract class JsonJitSerializer<T>
    {
        public JsonSerializerOptions Options { get; private set; }

        protected JsonWriterOptions WriterOptions
        {
            get
            {
                return new JsonWriterOptions
                {
                    Encoder = Options.Encoder,
                    Indented = Options.WriteIndented,
#if !DEBUG
                    SkipValidation = true
#endif
                };
            }
        }

        public static JsonJitSerializer<T> Compile(JsonSerializerOptions options = null)
        {
            Type jitSerializerType = JitILCompiler.Compile(typeof(T), ref options);
            JsonJitSerializer<T> serializer = (JsonJitSerializer<T>)Activator.CreateInstance(typeof(JsonJitSerializer<,>).MakeGenericType(typeof(T), jitSerializerType));
            serializer.Options = options;
            return serializer;
        }

        public string Serialize(T value)
        {
            using (PooledByteBufferWriter output = new PooledByteBufferWriter(Options.DefaultBufferSize))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output, WriterOptions))
            {
                Serialize(writer, value);
                return Encoding.UTF8.GetString(output.WrittenMemory.Span);
            }
        }

        public byte[] SerializeToUtf8Bytes(T value)
        {
            using (PooledByteBufferWriter output = new PooledByteBufferWriter(Options.DefaultBufferSize))
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output, WriterOptions))
            {
                Serialize(writer, value);
                return output.WrittenMemory.ToArray();
            }
        }

        public abstract void Serialize(Utf8JsonWriter writer, T value);

        public abstract Task SerializeAsync(Stream utf8Stream, T value, CancellationToken cancellationToken = default);

    }

    public sealed class JsonJitSerializer<TValue, TSerializerImplementation> : JsonJitSerializer<TValue>
        where TSerializerImplementation : struct, ISerialierImplementation<TValue>
    {
        public override void Serialize(Utf8JsonWriter writer, TValue value)
        {
            TSerializerImplementation implementation = default(TSerializerImplementation); // Default struct constructor
            implementation.Serialize(writer, value);
            writer.Flush();
        }

        public override async Task SerializeAsync(Stream utf8Stream, TValue value, CancellationToken cancellationToken)
        {
            TSerializerImplementation implementation = default(TSerializerImplementation); // Default struct constructor

            // Use the same logic as System.Json.Test
            using (var bufferWriter = new PooledByteBufferWriter(Options.DefaultBufferSize))
            using (var writer = new Utf8JsonWriter(bufferWriter, WriterOptions))
            {
                int flushThreshold = GetFlushThresholdFromBuffer(bufferWriter);
                while (implementation.SerializeChunk(writer, value))
                {
                    if (writer.BytesPending > flushThreshold)
                    {
                        writer.Flush();
                        await bufferWriter.WriteToStreamAsync(utf8Stream, cancellationToken).ConfigureAwait(false);
                        bufferWriter.Clear();
                    }
                }

                writer.Flush();
                await bufferWriter.WriteToStreamAsync(utf8Stream, cancellationToken).ConfigureAwait(false);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetFlushThresholdFromBuffer(PooledByteBufferWriter buffer) => (int)(buffer.Capacity * .9); //todo: determine best value here
    }
}
