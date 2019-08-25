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
        internal static readonly JsonSerializerOptions DEFAULT_SERIALIZER_OPTIONS = new JsonSerializerOptions();

        private static readonly Lazy<ModuleBuilder> _jitModuleBuilder = new Lazy<ModuleBuilder>(InitializeModuleBuilder);
        internal static ModuleBuilder JitModuleBuilder { get => _jitModuleBuilder.Value; }

        public static readonly string GENERATED_MODULE_NAMESPACE = @"MartinCl2.Text.Json.Serialization.Dynamic";

        public static string Serialize<T>(T value, JsonSerializerOptions options = null) => JsonJitSerializer<T>.Serialize(value, options);

        private static ModuleBuilder InitializeModuleBuilder()
        {
            AssemblyBuilder ab =
                AssemblyBuilder.DefineDynamicAssembly(
                    new AssemblyName(GENERATED_MODULE_NAMESPACE),
                    AssemblyBuilderAccess.Run);

            ModuleBuilder mb =
                ab.DefineDynamicModule(GENERATED_MODULE_NAMESPACE);

            return mb;
        }
    }

    public static class JsonJitSerializer<T>
    {
        private delegate bool SerializeChunkDelegate(Utf8JsonWriter writer, T value, JsonSerializerOptions options);

        private static Lazy<SerializeChunkDelegate> _impl = new Lazy<SerializeChunkDelegate>(Compile, true);
        private static SerializeChunkDelegate SerializeChunk => _impl.Value;

        public static void Initialize()
        {
            var _ = SerializeChunk;
        }

        public static string Serialize(T value, JsonSerializerOptions options = null)
        {
            if (options == null) {
                options = JsonJitSerializer.DEFAULT_SERIALIZER_OPTIONS;
            }
            SerializeChunkDelegate serializeChunk = SerializeChunk;
            ArrayBufferWriter<byte> output = new ArrayBufferWriter<byte>();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(output))
            {
                while (serializeChunk(writer, value, options));
            }
            return System.Text.Encoding.UTF8.GetString(output.WrittenSpan);
        }

        private static SerializeChunkDelegate Compile()
        {
            // TODO: array root
            return CompileObjectRoot();
        }

        private static SerializeChunkDelegate CompileObjectRoot()
        {
            DynamicMethod method = new DynamicMethod(
                name: "SerializeImplementation",
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(bool),
                new Type[] { typeof(Utf8JsonWriter), typeof(T), typeof(JsonSerializerOptions) },
                owner: typeof(JsonJitSerializer<T>),
                skipVisibility: false
            );

            ILGenerator ilg = method.GetILGenerator();

            // First agrument: Utf8JsonWriter writer
            // Second argument: T obj
            // Third argument: JsonSerializerOptions options

            // writer.WriteStartObject();
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Call, typeof(Utf8JsonWriter).GetMethod("WriteStartObject", new Type[] { }));

            // SerializeObjectContent(writer, obj, options)
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Ldarg_1);
            ilg.Emit(OpCodes.Ldarg_2);
            ilg.Emit(OpCodes.Call, JsonObjectJitILCompiler<T>.CompiledMethod);
            ilg.Emit(OpCodes.Pop);

            // writer.WriteEndObject();
            ilg.Emit(OpCodes.Ldarg_0);
            ilg.Emit(OpCodes.Call, typeof(Utf8JsonWriter).GetMethod("WriteEndObject", new Type[] { }));

            // return false;
            ilg.Emit(OpCodes.Ldc_I4_0);
            ilg.Emit(OpCodes.Ret);

            return (SerializeChunkDelegate)method.CreateDelegate(typeof(SerializeChunkDelegate));
        }
    }
}
