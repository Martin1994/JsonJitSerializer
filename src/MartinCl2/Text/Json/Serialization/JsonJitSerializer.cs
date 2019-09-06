using System;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MartinCl2.Text.Json.Serialization.Compiler;

namespace MartinCl2.Text.Json.Serialization
{
    public abstract class JsonJitSerializer<T>
    {
        public static JsonJitSerializer<T> Compile(JsonSerializerOptions options = null)
        {
            Type jitSerializerType = JitILCompiler.Compile(typeof(T), options);
            return (JsonJitSerializer<T>)Activator.CreateInstance(typeof(JsonJitSerializer<,>).MakeGenericType(typeof(T), jitSerializerType));
        }

        public abstract string Serialize(T value);

        public abstract void Serialize(Utf8JsonWriter writer, T value);

        public abstract Task SerializeAsync(Utf8JsonWriter writer, T value);

    }

    public sealed class JsonJitSerializer<TValue, TSerializerImplementation> : JsonJitSerializer<TValue>
        where TSerializerImplementation : struct, ISerialierImplementation<TValue>
    {
        public override string Serialize(TValue value)
        {
            ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output))
            {
                TSerializerImplementation implementation = default(TSerializerImplementation); // Default struct constructor
                implementation.Serialize(writer, value);
            }
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }

        public override void Serialize(Utf8JsonWriter writer, TValue value)
        {
            TSerializerImplementation implementation = default(TSerializerImplementation); // Default struct constructor
            implementation.Serialize(writer, value);
            writer.Flush();
        }

        public override async Task SerializeAsync(Utf8JsonWriter writer, TValue value)
        {
            TSerializerImplementation implementation = default(TSerializerImplementation); // Default struct constructor
            implementation.Reset();
            while (implementation.SerializeChunk(writer, value))
            {
                await writer.FlushAsync();
            }
            await writer.FlushAsync();
        }
    }
}
