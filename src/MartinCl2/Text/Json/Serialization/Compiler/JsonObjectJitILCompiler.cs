using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MartinCl2.Text.Json.Serialization.Compiler
{
    public struct JsonObjectJitILCompiler<T>
    {

        private static readonly MethodInfo _writePropertyName = typeof(Utf8JsonWriter).GetMethod("WritePropertyName", new Type[] { typeof(string) });
        private static readonly MethodInfo _writeStringValue = typeof(Utf8JsonWriter).GetMethod("WriteStringValue", new Type[] { typeof(string) });
        private static readonly MethodInfo _writeStartObject = typeof(Utf8JsonWriter).GetMethod("WriteStartObject", new Type[] { });
        private static readonly MethodInfo _writeEndObject = typeof(Utf8JsonWriter).GetMethod("WriteEndObject", new Type[] { });

        private static readonly Lazy<MethodInfo> _compiledMethod = new Lazy<MethodInfo>(Compile);
        public static MethodInfo CompiledMethod => _compiledMethod.Value;

        private static MethodInfo Compile()
        {
            TypeBuilder tb = JsonJitSerializer.JitModuleBuilder.DefineType(
                JsonJitSerializer.GENERATED_MODULE_NAMESPACE + @".ObjectSerialier_" + typeof(T).FullName.Replace('.', '_'));

            MethodBuilder mb = tb.DefineMethod(
                name: @"Serialize",
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(bool),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), typeof(T), typeof(JsonSerializerOptions) }
            );

            JsonObjectJitILCompiler<T> compiler = new JsonObjectJitILCompiler<T>(tb, mb);
            Action<Type>[] followUps = compiler.GenerateIL().ToArray();

            Type compiledType = tb.CreateType();

            foreach (Action<Type> followUp in followUps)
            {
                followUp(compiledType);
            }

            return compiledType.GetMethod(
                name: @"Serialize",
                types: new Type[] { typeof(Utf8JsonWriter), typeof(T), typeof(JsonSerializerOptions) }
            );
        }

        private readonly ILGenerator _ilg;

        private readonly TypeBuilder _tb;

        private JsonObjectJitILCompiler(TypeBuilder tb, MethodBuilder mb)
        {
            _tb = tb;
            _ilg = mb.GetILGenerator();
        }

        private IEnumerable<Action<Type>> GenerateIL()
        {
            // First agrument: Utf8JsonWriter writer
            // Second argument: T obj
            // Third argument: JsonSerializerOptions options

            // JsonNamingPolicy namingPolicy = options.PropertyNamingPolicy;
            LocalBuilder namingPolicyLocal = _ilg.DeclareLocal(typeof(JsonNamingPolicy));
            _ilg.Emit(OpCodes.Ldarg_2);
            _ilg.Emit(OpCodes.Call, typeof(JsonSerializerOptions).GetProperty("PropertyNamingPolicy").GetMethod);
            _ilg.Emit(OpCodes.Stloc, namingPolicyLocal);

            foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                // Skip special properties
                if (property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                MethodInfo getMethod = property.GetMethod;
                if (getMethod == null || !getMethod.IsPublic)
                {
                    continue;
                }

                GenerateILForWritingPropertyName(property, namingPolicyLocal);

                // Save converter to a global variable
                JsonConverter converter = DetermineConverterForProperty(
                    options: JsonJitSerializer.DEFAULT_SERIALIZER_OPTIONS,
                    runtimePropertyType: property.PropertyType,
                    propertyInfo: property
                );

                if (converter == null)
                {
                    // Nested type
                    // writer.WriteStartObject();
                    _ilg.Emit(OpCodes.Ldarg_0);
                    _ilg.Emit(OpCodes.Call, _writeStartObject);
                    // SerializeObjectContent(writer, obj.Property, options)
                    _ilg.Emit(OpCodes.Ldarg_0);
                    _ilg.Emit(OpCodes.Ldarg_1);
                    GenerateILforCallingGetMethod(getMethod);
                    _ilg.Emit(OpCodes.Ldarg_2);
                    _ilg.Emit(
                        OpCodes.Call,
                        typeof(JsonObjectJitILCompiler<>)
                            .MakeGenericType(property.PropertyType)
                            .GetProperty("CompiledMethod")
                            .GetValue(null) as MethodInfo
                    );
                    _ilg.Emit(OpCodes.Pop);
                    // writer.WriteEndObject();
                    _ilg.Emit(OpCodes.Ldarg_0);
                    _ilg.Emit(OpCodes.Call, _writeEndObject);
                    continue;
                }

                yield return GenerateILForCallingConverter(property, getMethod, converter);
            }

            // return false;
            _ilg.Emit(OpCodes.Ldc_I4_0);
            _ilg.Emit(OpCodes.Ret);
        }

        private void GenerateILForWritingPropertyName(PropertyInfo property, LocalBuilder namingPolicyLocal)
        {
            // writer.WritePropertyName(namingPolicy != null ? namingPolicy.ConvertName("property.Name") : "property.Name");
            _ilg.Emit(OpCodes.Ldarg_0);
            Label namingPolicyIsNull = _ilg.DefineLabel();
            Label nameConverted = _ilg.DefineLabel();
            // namingPolicy != null ?
            _ilg.Emit(OpCodes.Ldloc, namingPolicyLocal);
            _ilg.Emit(OpCodes.Ldnull);
            _ilg.Emit(OpCodes.Beq, namingPolicyIsNull);
            // namingPolicy.ConvertName("property.Name")
            _ilg.Emit(OpCodes.Ldloc, namingPolicyLocal);
            _ilg.Emit(OpCodes.Ldstr, property.Name);
            _ilg.Emit(OpCodes.Callvirt, typeof(JsonNamingPolicy).GetMethod("ConvertName", new Type[] {
                typeof(string)
            }));
            _ilg.Emit(OpCodes.Br, nameConverted);
            _ilg.MarkLabel(namingPolicyIsNull);
            // "property.Name"
            _ilg.Emit(OpCodes.Ldstr, property.Name);
            _ilg.MarkLabel(nameConverted);
            _ilg.Emit(OpCodes.Call, _writePropertyName);
        }

        private Action<Type> GenerateILForCallingConverter(PropertyInfo property, MethodInfo getMethod, JsonConverter converter)
        {
            if (!converter.CanConvert(property.PropertyType))
            {
                throw new InvalidOperationException("Converter is not compatible.");
            }

            Type converterType = converter.GetType();
            Type converterGenericTypeParameter = GetConverterGenericType(converterType);
            Type converterGenericType = typeof(JsonConverter<>).MakeGenericType(new Type[] {
                converterGenericTypeParameter
            });

            string fieldName = @"_converterFor" + property.Name;
            FieldBuilder converterField = _tb.DefineField(
                fieldName: fieldName,
                // Default converters are internal classes so here the abstract generic type must be used.
                type: converterGenericType,
                attributes: FieldAttributes.Public | FieldAttributes.Static
            );

            // converter.Write(writer, obj.Property, options);
            _ilg.Emit(OpCodes.Ldsfld, converterField);
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldarg_1);
            GenerateILforCallingGetMethod(getMethod);
            _ilg.Emit(OpCodes.Ldarg_2);
            _ilg.Emit(OpCodes.Callvirt, converterGenericType.GetMethod("Write", new Type[] {
                typeof(Utf8JsonWriter),
                converterGenericTypeParameter,
                typeof(JsonSerializerOptions)
            }));

            return type => {
                FieldInfo field = type.GetField(fieldName);
                field.SetValue(null, converter);
            };
        }

        private void GenerateILforCallingGetMethod(MethodInfo getMethod)
        {
            if (!getMethod.IsVirtual || getMethod.IsFinal)
            {
                _ilg.Emit(OpCodes.Call, getMethod);
            }
            else
            {
                _ilg.Emit(OpCodes.Callvirt, getMethod);
            }
        }

        private static Type GetConverterGenericType(Type converterType)
        {
            while (!converterType.IsGenericType || converterType.GetGenericTypeDefinition() != typeof(JsonConverter<>))
            {
                converterType = converterType.BaseType;
            }

            return converterType.GetGenericArguments()[0];
        }

        // ========== Modified from CoreFX ==========
        // Licensed to the .NET Foundation under one or more agreements.
        // The .NET Foundation licenses this file to you under the MIT license.
        // See the LICENSE file in the project root for more information.
        public static JsonConverter DetermineConverterForProperty(JsonSerializerOptions options, Type runtimePropertyType, PropertyInfo propertyInfo)
        {
            JsonConverter converter = null;

            if (propertyInfo != null)
            {
                JsonConverterAttribute converterAttribute = (JsonConverterAttribute)propertyInfo.GetCustomAttributes(typeof(JsonConverterAttribute), inherit: false)
                    .FirstOrDefault();

                if (converterAttribute != null)
                {
                    converter = GetConverterFromAttribute(converterAttribute, typeToConvert: runtimePropertyType);
                }
            }

            if (converter == null)
            {
                converter = options.GetConverter(runtimePropertyType);
            }

            if (converter is JsonConverterFactory factory)
            {
                converter = factory.CreateConverter(runtimePropertyType, options);
            }

            return converter;
        }

        private static JsonConverter GetConverterFromAttribute(JsonConverterAttribute converterAttribute, Type typeToConvert)
        {
            JsonConverter converter;

            Type type = converterAttribute.ConverterType;
            if (type == null)
            {
                // Allow the attribute to create the converter.
                converter = converterAttribute.CreateConverter(typeToConvert);
                if (converter == null)
                {
                    throw new InvalidOperationException();
                }
            }
            else
            {
                ConstructorInfo ctor = type.GetConstructor(Type.EmptyTypes);
                if (!typeof(JsonConverter).IsAssignableFrom(type) || !ctor.IsPublic)
                {
                    throw new InvalidOperationException();
                }

                converter = (JsonConverter)Activator.CreateInstance(type);
            }

            if (!converter.CanConvert(typeToConvert))
            {
                throw new InvalidOperationException();
            }

            return converter;
        }
    }
}
