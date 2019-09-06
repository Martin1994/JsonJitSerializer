using System;
using System.Buffers;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MartinCl2.Text.Json.Serialization.Compiler;

namespace MartinCl2.Text.Json.Serialization
{
    public static class JsonJitSerializer
    {
        internal static readonly JsonSerializerOptions DefaultSerializerOptions = new JsonSerializerOptions();

        public static JsonJitSerializer<T> Compile<T>(JsonSerializerOptions options = null)
        {
            if (options == null)
            {
                options = DefaultSerializerOptions;
            }

            Type jitSerializerType = JitILCompiler.Compile(typeof(T), options);
            return (JsonJitSerializer<T>)Activator.CreateInstance(typeof(JsonJitSerializer<,>).MakeGenericType(typeof(T), jitSerializerType));
        }
    }

    public abstract class JsonJitSerializer<T>
    {
        public abstract string Serialize(T value);

        public abstract Task<string> SerializeAsync(T value);

    }

    public sealed class JsonJitSerializer<TValue, TSerializer> : JsonJitSerializer<TValue> 
        where TSerializer : ISerialierImplementation<TValue>
    {
        public override string Serialize(TValue value)
        {
            ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output))
            {
                TSerializer jitSerializer = default(TSerializer); // TSerializer should be a struct
                jitSerializer.Serialize(writer, value);
            }
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }

        public override async Task<string> SerializeAsync(TValue value)
        {
            ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output))
            {
                TSerializer jitSerializer = default(TSerializer); // TSerializer should be a struct
                jitSerializer.Reset();
                while (jitSerializer.SerializeChunk(writer, value))
                {
                    await writer.FlushAsync();
                }
                await writer.FlushAsync();
            }
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
    }
}
