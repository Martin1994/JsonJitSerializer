using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace MartinCl2.Text.Json.Serialization.Compiler
{
    public struct JitILCompiler
    {

        private static readonly MethodInfo _writePropertyName = typeof(Utf8JsonWriter).GetMethod("WritePropertyName", new Type[] { typeof(string) });
        private static readonly MethodInfo _writeStringValue = typeof(Utf8JsonWriter).GetMethod("WriteStringValue", new Type[] { typeof(string) });
        private static readonly MethodInfo _writeStartObject = typeof(Utf8JsonWriter).GetMethod("WriteStartObject", new Type[] { });
        private static readonly MethodInfo _writeEndObject = typeof(Utf8JsonWriter).GetMethod("WriteEndObject", new Type[] { });
        private static readonly MethodInfo _writeStartArray = typeof(Utf8JsonWriter).GetMethod("WriteStartArray", new Type[] { });
        private static readonly MethodInfo _writeEndArray = typeof(Utf8JsonWriter).GetMethod("WriteEndArray", new Type[] { });
        private static readonly MethodInfo _writeNullValue = typeof(Utf8JsonWriter).GetMethod("WriteNullValue", new Type[] { });

        private static int _compiledCount = 0;

        private static readonly string SerializeMethodName = @"Serialize";

        public static MethodInfo Compile(Type type, JsonSerializerOptions options)
        {
            TypeBuilder tb = JitAssembly.JitModuleBuilder.DefineType(
                JitAssembly.GeneratedModuleNamespace + @".ObjectSerialier" + (Interlocked.Increment(ref _compiledCount) - 1));

            JitILCompiler compiler = new JitILCompiler(type, options, tb);
            Action<Type>[] followUps = compiler.GenerateIL().ToArray();

            Type compiledType = tb.CreateType();

            foreach (Action<Type> followUp in followUps)
            {
                followUp(compiledType);
            }

            return compiledType.GetMethod(
                name: SerializeMethodName,
                types: new Type[] { typeof(Utf8JsonWriter), type }
            );
        }

        private readonly Type _type;

        private readonly JsonSerializerOptions _options;

        private readonly ILGenerator _ilg;

        private readonly TypeBuilder _tb;

        private JitILCompiler(Type type, JsonSerializerOptions options, TypeBuilder tb)
        {
            MethodBuilder mb = tb.DefineMethod(
                name: SerializeMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Static,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(bool),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), type }
            );

            _type = type;
            _options = options;
            _tb = tb;
            _ilg = mb.GetILGenerator();
        }

        private IEnumerable<Action<Type>> GenerateIL()
        {
            // First agrument: Utf8JsonWriter writer
            // Second argument: T obj

            // Define a static field for JsonSerializerOptions
            JsonSerializerOptions options = _options;
            string optionsFieldName = @"_options";
            FieldBuilder optionsField = _tb.DefineField(
                fieldName: optionsFieldName,
                type: typeof(JsonSerializerOptions),
                attributes: FieldAttributes.Public | FieldAttributes.Static
            );
            yield return type => {
                FieldInfo field = type.GetField(optionsFieldName);
                field.SetValue(null, options);
            };

            Label objIsNull = _ilg.DefineLabel();
            if (!_type.IsValueType)
            {
                // if (obj != null) {
                _ilg.Emit(OpCodes.Ldarg_1);
                _ilg.Emit(OpCodes.Ldnull);
                _ilg.Emit(OpCodes.Beq, objIsNull);
            }

            if (_type.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            {
                foreach (Action<Type> followUp in GenerateILForIEnumerable(optionsField)) {
                    yield return followUp;
                }
            }
            else
            {
                foreach (Action<Type> followUp in GenerateILForObject(optionsField)) {
                    yield return followUp;
                }
            }

            // return false;
            _ilg.Emit(OpCodes.Ldc_I4_0);
            _ilg.Emit(OpCodes.Ret);

            if (!_type.IsValueType)
            {
                // } else {
                _ilg.MarkLabel(objIsNull);
                // writer.WriteNullValue();
                _ilg.Emit(OpCodes.Ldarg_0);
                _ilg.Emit(OpCodes.Call, _writeNullValue);

                // return false;
                _ilg.Emit(OpCodes.Ldc_I4_0);
                _ilg.Emit(OpCodes.Ret);

                // }
            }
        }

        private IEnumerable<Action<Type>> GenerateILForObject(FieldBuilder optionsField)
        {
            // First agrument: Utf8JsonWriter writer
            // Second argument: T obj

            // writer.WriteStartObject();
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Call, _writeStartObject);

            foreach (PropertyInfo property in _type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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

                GenerateILForWritingPropertyName(property);

                JsonConverter converter = DetermineConverterForProperty(
                    options: _options,
                    runtimePropertyType: property.PropertyType,
                    propertyInfo: property
                );

                JitILCompiler that = this;
                Action pushPropertyValueOntoStack = () => {
                    // obj.Property
                    that._ilg.Emit(OpCodes.Ldarg_1);
                    that.GenerateILforCallingGetMethod(getMethod);
                };

                if (converter == null)
                {
                    // Nested type
                    // ObjectSerialierX.Serialize(writer, obj.Property)
                    _ilg.Emit(OpCodes.Ldarg_0);
                    pushPropertyValueOntoStack();
                    _ilg.Emit(OpCodes.Call, JitILCompiler.Compile(property.PropertyType, _options));
                    _ilg.Emit(OpCodes.Pop);
                } else {
                    yield return GenerateILForCallingConverter(property.PropertyType, property.Name, pushPropertyValueOntoStack, converter, optionsField);
                }
            }

            // writer.WriteEndObject();
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Call, _writeEndObject);
        }

        private IEnumerable<Action<Type>> GenerateILForIEnumerable(FieldBuilder optionsField)
        {
            // First agrument: Utf8JsonWriter writer
            // Second argument: IEnumerable<T> enumerable

            Type elementType = GetIEnumerableGenericType(_type);
            Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
            Type enumeratorType = typeof(IEnumerator<>).MakeGenericType(elementType);

            // writer.WriteStartArray();
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Call, _writeStartArray);

            // IEnumerator<T> it = enumerable.GetEnumerator();
            LocalBuilder itLocal = _ilg.DeclareLocal(typeof(IEnumerator<>).MakeGenericType(elementType));
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Callvirt, enumerableType.GetMethod("GetEnumerator", new Type[] { }));
            _ilg.Emit(OpCodes.Stloc, itLocal);

            // while (it.MoveNext()) {
            Label topOfIterationLoop = _ilg.DefineLabel();
            Label bottomOfIterationLoop = _ilg.DefineLabel();
            _ilg.MarkLabel(topOfIterationLoop);
            _ilg.Emit(OpCodes.Ldloc, itLocal);
            // MoveNext is defined in IEnumerator
            _ilg.Emit(OpCodes.Callvirt, typeof(IEnumerator).GetMethod("MoveNext", new Type[] { }));
            _ilg.Emit(OpCodes.Brfalse, bottomOfIterationLoop);

            // Save converter to a global variable
            JsonConverter converter = _options.GetConverter(elementType);

            JitILCompiler that = this;
            Action pushNextElementOntoStack = () => {
                // it.Current
                that._ilg.Emit(OpCodes.Ldloc, itLocal);
                that._ilg.Emit(OpCodes.Callvirt, enumeratorType.GetProperty("Current").GetMethod);
            };

            if (converter == null)
            {
                // Nested type
                // ObjectSerialierX.Serialize(writer, obj.Property)
                _ilg.Emit(OpCodes.Ldarg_0);
                pushNextElementOntoStack();
                _ilg.Emit(OpCodes.Call, JitILCompiler.Compile(elementType, _options));
                _ilg.Emit(OpCodes.Pop);
            } else {
                yield return GenerateILForCallingConverter(elementType, "Element", pushNextElementOntoStack, converter, optionsField);
            }

            // }
            _ilg.Emit(OpCodes.Br, topOfIterationLoop);
            _ilg.MarkLabel(bottomOfIterationLoop);

            // writer.WriteEndArray();
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Call, _writeEndArray);
        }

        private void GenerateILForWritingPropertyName(PropertyInfo property)
        {
            JsonNamingPolicy policy = _options.PropertyNamingPolicy;
            JsonPropertyNameAttribute nameAttribute = (JsonPropertyNameAttribute)property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
                    .FirstOrDefault();

            // writer.WritePropertyName("convertedName");
            _ilg.Emit(OpCodes.Ldarg_0);
            if (nameAttribute != null) {
                _ilg.Emit(OpCodes.Ldstr, nameAttribute.Name);
            } else if (policy == null) {
                _ilg.Emit(OpCodes.Ldstr, property.Name);
            } else {
                _ilg.Emit(OpCodes.Ldstr, policy.ConvertName(property.Name));
            }

            _ilg.Emit(OpCodes.Call, _writePropertyName);
        }

        private Action<Type> GenerateILForCallingConverter(Type valueType, string valueName, Action pushValueOntoStack, JsonConverter converter, FieldInfo optionsField)
        {
            if (!converter.CanConvert(valueType))
            {
                throw new InvalidOperationException("Converter is not compatible.");
            }

            Type converterType = converter.GetType();
            Type converterGenericTypeParameter = GetConverterGenericType(converterType);
            Type converterGenericType = typeof(JsonConverter<>).MakeGenericType(new Type[] {
                converterGenericTypeParameter
            });

            // Save converter to a global variable
            string fieldName = @"_converterFor" + valueName;
            FieldBuilder converterField = _tb.DefineField(
                fieldName: fieldName,
                // Default converters are internal classes so here the abstract generic type must be used.
                type: converterGenericType,
                attributes: FieldAttributes.Public | FieldAttributes.Static
            );

            // converter.Write(writer, obj.Property, _options);
            _ilg.Emit(OpCodes.Ldsfld, converterField);
            _ilg.Emit(OpCodes.Ldarg_0);
            pushValueOntoStack();
            _ilg.Emit(OpCodes.Ldsfld, optionsField);
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

        private static Type GetConverterGenericType(Type converterType) => GetGenericTypesFromParent(converterType, typeof(JsonConverter<>))[0];

        private static Type[] GetGenericTypesFromParent(Type subType, Type genericType)
        {
            while (!subType.IsGenericType || subType.GetGenericTypeDefinition() != genericType)
            {
                subType = subType.BaseType;
            }

            return subType.GetGenericArguments();
        }

        private static Type GetIEnumerableGenericType(Type ienumerableType)
        {
            return ienumerableType
                .GetInterfaces()
                .First(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];
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
