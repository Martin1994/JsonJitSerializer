using System;
using System.Buffers;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
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
            // TODO: IList, IDictionary, null check
            return new JsonJitSerializer<T>(
                JitILCompiler
                    .Compile(typeof(T), options)
                    .CreateDelegate(typeof(JsonJitSerializer<T>.SerializeChunkDelegate))
                as JsonJitSerializer<T>.SerializeChunkDelegate
            );
        }
    }

    public class JsonJitSerializer<T>
    {
        public delegate bool SerializeChunkDelegate(Utf8JsonWriter writer, T obj);
        private readonly SerializeChunkDelegate _serializeChunk;

        public JsonJitSerializer(SerializeChunkDelegate serializeChunk)
        {
            _serializeChunk = serializeChunk;
        }

        public string Serialize(T value)
        {
            ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output))
            {
                while (_serializeChunk(writer, value));
            }
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
    }
}
