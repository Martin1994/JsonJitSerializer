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

        private static int _compiledCount = -1;

        private static readonly string SerializeMethodName = @"Serialize";
        private static readonly string SerializeChunkMethodName = @"SerializeChunk";

        public static Type Compile(Type payloadType, JsonSerializerOptions options)
        {
            TypeBuilder tb = JitAssembly.JitModuleBuilder.DefineType(
                name: JitAssembly.GeneratedModuleNamespace + @".ObjectSerialier" + Interlocked.Increment(ref _compiledCount),
                attr: TypeAttributes.Public | TypeAttributes.Sealed,
                parent: typeof(ValueType),
                interfaces: new Type[] { typeof(IObjectSerialier<>).MakeGenericType(payloadType) }
            );

            JitILCompiler compiler = new JitILCompiler(payloadType, options, tb);
            compiler.GenerateChunkIL();
            compiler.GenerateNonChunkIL();

            Type compiledSerializerType = tb.CreateType();

            compiler._followUp(compiledSerializerType);

            return compiledSerializerType;
        }

        private Action<Type> _followUp;

        private readonly Type _type;

        private readonly JsonSerializerOptions _options;

        private readonly FieldBuilder _optionsField;

        private ILGenerator _ilg;

        private bool _generatingChunkMethod;

        private readonly TypeBuilder _tb;

        private readonly List<FieldBuilder> _writeStack;

        private int _converterCounter;

        private readonly Dictionary<JsonConverter, FieldBuilder> _converterCache;

        private JitILCompiler(Type type, JsonSerializerOptions options, TypeBuilder tb)
        {
            _type = type;
            _options = options;
            _tb = tb;
            _ilg = null;
            _followUp = null;
            _writeStack = new List<FieldBuilder>();
            _converterCounter = -1;
            _converterCache = new Dictionary<JsonConverter, FieldBuilder>();
            _generatingChunkMethod = default(bool);
            
            // Define a static field for JsonSerializerOptions
            string optionsFieldName = @"Options";
            _optionsField = _tb.DefineField(
                fieldName: optionsFieldName,
                type: typeof(JsonSerializerOptions),
                attributes: FieldAttributes.Public | FieldAttributes.Static
            );
            _followUp += type =>
            {
                FieldInfo field = type.GetField(optionsFieldName);
                field.SetValue(null, options);
            };
        }

        private void GenerateChunkIL()
        {
            _generatingChunkMethod = true;
            
            MethodBuilder mb = _tb.DefineMethod(
                name: SerializeChunkMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Virtual,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(bool),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), _type }
            );

            _tb.DefineMethodOverride(mb, typeof(IObjectSerialier<>).MakeGenericType(_type).GetMethod(SerializeChunkMethodName));

            _ilg = mb.GetILGenerator();

            // arg0: ObjectSerialier this
            // arg1: Utf8JsonWriter writer
            // arg2: T obj

            // _writeStack0 = obj;
            FieldInfo writeStackRoot = GetWriteStackFieldAtDepth(0);
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldarg_2);
            _ilg.Emit(OpCodes.Stfld, writeStackRoot);
            GenerateILForProperty(_type, 0);

            // return false;
            _ilg.Emit(OpCodes.Ldc_I4_0);
            _ilg.Emit(OpCodes.Ret);
        }

        private void GenerateNonChunkIL()
        {
            _generatingChunkMethod = false;
            
            MethodBuilder mb = _tb.DefineMethod(
                name: SerializeMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Virtual,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(void),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), _type }
            );

            _tb.DefineMethodOverride(mb, typeof(IObjectSerialier<>).MakeGenericType(_type).GetMethod(SerializeMethodName));

            _ilg = mb.GetILGenerator();

            // arg0: ObjectSerialier this
            // arg1: Utf8JsonWriter writer
            // arg2: T obj

            // _writeStack0 = obj;
            FieldInfo writeStackRoot = GetWriteStackFieldAtDepth(0);
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldarg_2);
            _ilg.Emit(OpCodes.Stfld, writeStackRoot);
            GenerateILForProperty(_type, 0);

            // return;
            _ilg.Emit(OpCodes.Ret);
        }

        private void GenerateILForProperty(Type currentType, int depth)
        {
            Label objIsNull = _ilg.DefineLabel();
            Label endOfSerailation = _ilg.DefineLabel();
            if (!currentType.IsValueType)
            {
                // if (obj != null) {
                _ilg.Emit(OpCodes.Ldarg_0);
                _ilg.Emit(OpCodes.Ldfld, GetWriteStackFieldAtDepth(depth));
                _ilg.Emit(OpCodes.Ldnull);
                _ilg.Emit(OpCodes.Beq, objIsNull);
            }

            if (currentType.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            {
                GenerateILForIEnumerable(currentType, depth);
            }
            else
            {
                GenerateILForObject(currentType, depth);
            }

            if (!currentType.IsValueType)
            {
                // } else {
                _ilg.Emit(OpCodes.Br, endOfSerailation);
                _ilg.MarkLabel(objIsNull);

                // writer.WriteNullValue();
                _ilg.Emit(OpCodes.Ldarg_1);
                _ilg.Emit(OpCodes.Call, _writeNullValue);

                // }
                _ilg.MarkLabel(endOfSerailation);
            }
        }

        private void GenerateILForObject(Type currentType, int depth)
        {
            // writer.WriteStartObject();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeStartObject);

            foreach (PropertyInfo property in currentType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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
                    that._ilg.Emit(OpCodes.Ldarg_0);
                    that._ilg.Emit(OpCodes.Ldfld, that.GetWriteStackFieldAtDepth(depth));
                    that.GenerateILforCallingGetMethod(getMethod);
                };

                if (converter == null)
                {
                    GenerateILForNestedType(property.PropertyType, depth, pushPropertyValueOntoStack);
                } else {
                    GenerateILForCallingConverter(property.PropertyType, pushPropertyValueOntoStack, converter);
                }
            }

            // writer.WriteEndObject();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeEndObject);
        }

        private void GenerateILForIEnumerable(Type currentType, int depth)
        {
            // First agrument: Utf8JsonWriter writer
            // Second argument: IEnumerable<T> enumerable

            Type elementType = GetIEnumerableGenericType(currentType);
            Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
            Type enumeratorType = typeof(IEnumerator<>).MakeGenericType(elementType);

            // writer.WriteStartArray();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeStartArray);

            // IEnumerator<T> it = enumerable.GetEnumerator();
            LocalBuilder itLocal = _ilg.DeclareLocal(typeof(IEnumerator<>).MakeGenericType(elementType));
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldfld, GetWriteStackFieldAtDepth(depth));
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
                GenerateILForNestedType(elementType, depth, pushNextElementOntoStack);
            }
            else {
                GenerateILForCallingConverter(elementType, pushNextElementOntoStack, converter);
            }

            // }
            _ilg.Emit(OpCodes.Br, topOfIterationLoop);
            _ilg.MarkLabel(bottomOfIterationLoop);

            // writer.WriteEndArray();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeEndArray);
        }

        private void GenerateILForNestedType(Type nestedType, int depth, Action pushNestedValueOntoStack)
        {
            // _writeStackX = nextElement;
            _ilg.Emit(OpCodes.Ldarg_0);
            if (nestedType.IsValueType)
            {
                // TODO: avoid boxing
                _ilg.Emit(OpCodes.Box);
            }
            pushNestedValueOntoStack();
            _ilg.Emit(OpCodes.Stfld, GetWriteStackFieldAtDepth(depth + 1));
            GenerateILForProperty(nestedType, depth + 1);
        }

        private void GenerateILForWritingPropertyName(PropertyInfo property)
        {
            JsonNamingPolicy policy = _options.PropertyNamingPolicy;
            JsonPropertyNameAttribute nameAttribute = (JsonPropertyNameAttribute)property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
                    .FirstOrDefault();

            // writer.WritePropertyName("convertedName");
            _ilg.Emit(OpCodes.Ldarg_1);
            if (nameAttribute != null) {
                _ilg.Emit(OpCodes.Ldstr, nameAttribute.Name);
            } else if (policy == null) {
                _ilg.Emit(OpCodes.Ldstr, property.Name);
            } else {
                _ilg.Emit(OpCodes.Ldstr, policy.ConvertName(property.Name));
            }

            _ilg.Emit(OpCodes.Call, _writePropertyName);
        }

        private void GenerateILForCallingConverter(Type valueType, Action pushValueOntoStack, JsonConverter converter)
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
            FieldBuilder converterField;
            if (!_converterCache.TryGetValue(converter, out converterField)) {
                string fieldName = @"Converter" + (++_converterCounter);
                converterField = _tb.DefineField(
                    fieldName: fieldName,
                    // Default converters are internal classes so here the abstract generic type must be used.
                    type: converterGenericType,
                    attributes: FieldAttributes.Public | FieldAttributes.Static
                );

                _converterCache.Add(converter, converterField);

                _followUp += type => {
                    FieldInfo field = type.GetField(fieldName);
                    field.SetValue(null, converter);
                };
            }

            // converter.Write(writer, obj.Property, _options);
            _ilg.Emit(OpCodes.Ldsfld, converterField);
            _ilg.Emit(OpCodes.Ldarg_1);
            pushValueOntoStack();
            _ilg.Emit(OpCodes.Ldsfld, _optionsField);
            _ilg.Emit(OpCodes.Callvirt, converterGenericType.GetMethod("Write", new Type[] {
                typeof(Utf8JsonWriter),
                converterGenericTypeParameter,
                typeof(JsonSerializerOptions)
            }));
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

        private FieldBuilder GetWriteStackFieldAtDepth(int depth)
        {
            while (_writeStack.Count <= depth) {
                _writeStack.Add(_tb.DefineField(
                    fieldName: "_writeStack" + depth,
                    type: typeof(Object), // Used as void*
                    attributes: FieldAttributes.Private
                ));
            }
            return _writeStack[depth];
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
