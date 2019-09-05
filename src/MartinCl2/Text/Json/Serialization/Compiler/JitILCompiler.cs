using System;
using System.Collections;
using System.Collections.Generic;
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

        private static readonly string ResetMethodName = @"Reset";

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
            // Compile the non chunk routine first since the chunk routine needs to know the number of labels.
            compiler.GenerateNonChunkMethod();
            compiler.GenerateChunkMethod();
            compiler.GenerateResetMethod();

            Type compiledSerializerType = tb.CreateType();

            compiler._followUp(compiledSerializerType);

            return compiledSerializerType;
        }

        private Action<Type> _followUp;

        private readonly Type _rootType;

        private readonly JsonSerializerOptions _options;

        private readonly FieldBuilder _optionsField;

        private ILGenerator _ilg;

        private bool _generatingChunkMethod;

        private readonly TypeBuilder _tb;

        private readonly List<FieldBuilder> _writeStack;

        private int _converterCounter;

        private readonly Dictionary<JsonConverter, FieldBuilder> _converterCache;

        private Label[] _jumpTable;

        private int _jumpTableCount;

        private FieldBuilder _jumpTableProgressField;

        private JitILCompiler(Type type, JsonSerializerOptions options, TypeBuilder tb)
        {
            _rootType = type;
            _options = options;
            _tb = tb;
            _followUp = null;
            _writeStack = new List<FieldBuilder>();
            _converterCounter = -1;
            _converterCache = new Dictionary<JsonConverter, FieldBuilder>();

            // Don't care in constructor
            _ilg = null;
            _generatingChunkMethod = false;
            _jumpTable = null;
            _jumpTableCount = 0;

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

            // Define a field for jump table progress
            _jumpTableProgressField = _tb.DefineField(
                fieldName: @"_progress",
                type: typeof(int),
                attributes: FieldAttributes.Private
            );
        }

        private void GenerateResetMethod()
        {
            MethodBuilder mb = _tb.DefineMethod(
                name: ResetMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Virtual,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(void),
                parameterTypes: new Type[] { }
            );

            _tb.DefineMethodOverride(mb, typeof(IObjectSerialier<>).MakeGenericType(_rootType).GetMethod(ResetMethodName));

            _ilg = mb.GetILGenerator();

            // _progress = 0;
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldc_I4_0);
            _ilg.Emit(OpCodes.Stfld, _jumpTableProgressField);

            // return false;
            _ilg.Emit(OpCodes.Ret);
        }

        private void GenerateChunkMethod()
        {
            _generatingChunkMethod = true;

            MethodBuilder mb = _tb.DefineMethod(
                name: SerializeChunkMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Virtual,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(bool),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), _rootType }
            );

            _tb.DefineMethodOverride(mb, typeof(IObjectSerialier<>).MakeGenericType(_rootType).GetMethod(SerializeChunkMethodName));

            _ilg = mb.GetILGenerator();

            _jumpTable = new Label[_jumpTableCount];
            for (int i = 0; i < _jumpTableCount; i++)
            {
                _jumpTable[i] = _ilg.DefineLabel();
            }
            _jumpTableCount = 0;

            // arg0: ObjectSerialier this
            // arg1: Utf8JsonWriter writer
            // arg2: T obj

            // Jump to the point of progress
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldfld, _jumpTableProgressField);
            _ilg.Emit(OpCodes.Switch, _jumpTable);

            MarkNewJumpTableLabel();

            JitILCompiler that = this;
            GenerateILForNestedType(_rootType, 0, () => that._ilg.Emit(OpCodes.Ldarg_2));

            // return false;
            _ilg.Emit(OpCodes.Ldc_I4_0);
            _ilg.Emit(OpCodes.Ret);
        }

        private void GenerateNonChunkMethod()
        {
            _generatingChunkMethod = false;

            MethodBuilder mb = _tb.DefineMethod(
                name: SerializeMethodName,
                attributes: MethodAttributes.Public | MethodAttributes.Virtual,
                callingConvention: CallingConventions.Standard,
                returnType: typeof(void),
                parameterTypes: new Type[] { typeof(Utf8JsonWriter), _rootType }
            );

            _tb.DefineMethodOverride(mb, typeof(IObjectSerialier<>).MakeGenericType(_rootType).GetMethod(SerializeMethodName));

            _ilg = mb.GetILGenerator();

            _jumpTableCount = 0;

            // arg0: ObjectSerialier this
            // arg1: Utf8JsonWriter writer
            // arg2: T obj

            MarkNewJumpTableLabel();

            JitILCompiler that = this;
            GenerateILForNestedType(_rootType, 0, () => that._ilg.Emit(OpCodes.Ldarg_2));

            // return;
            _ilg.Emit(OpCodes.Ret);
        }

        private void MarkNewJumpTableLabel()
        {
            if (_generatingChunkMethod)
            {
                if (_jumpTableCount > 0 && _jumpTableCount < _jumpTable.Length - 1)
                {
                    // _progress = _jumpTableCount;
                    _ilg.Emit(OpCodes.Ldarg_0);
                    _ilg.Emit(OpCodes.Ldc_I4, _jumpTableCount);
                    _ilg.Emit(OpCodes.Stfld, _jumpTableProgressField);

                    // return true;
                    _ilg.Emit(OpCodes.Ldc_I4_1);
                    _ilg.Emit(OpCodes.Ret);
                }
                _ilg.MarkLabel(_jumpTable[_jumpTableCount]);
            }
            _jumpTableCount++;
        }

        private void GenerateILForNestedType(Type type, int depth, Action pushNestedValueOntoStack)
        {
            // _writeStackX = nextElement;
            _ilg.Emit(OpCodes.Ldarg_0);
            pushNestedValueOntoStack();
            if (type.IsValueType)
            {
                _ilg.Emit(OpCodes.Box, type);
            }
            _ilg.Emit(OpCodes.Stfld, GetWriteStackFieldAtDepth(depth));

            Label objIsNull = _ilg.DefineLabel();
            Label endOfSerailizaion = _ilg.DefineLabel();
            if (!type.IsValueType)
            {
                // if (obj != null) {
                _ilg.Emit(OpCodes.Ldarg_0);
                _ilg.Emit(OpCodes.Ldfld, GetWriteStackFieldAtDepth(depth));
                _ilg.Emit(OpCodes.Ldnull);
                _ilg.Emit(OpCodes.Beq, objIsNull);
            }

            if (type.GetInterfaces().Any(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
            {
                GenerateILForIEnumerable(type, depth);
            }
            else
            {
                GenerateILForObject(type, depth);
            }

            if (!type.IsValueType)
            {
                // } else {
                _ilg.Emit(OpCodes.Br, endOfSerailizaion);
                _ilg.MarkLabel(objIsNull);

                // writer.WriteNullValue();
                _ilg.Emit(OpCodes.Ldarg_1);
                _ilg.Emit(OpCodes.Call, _writeNullValue);

                // }
                _ilg.MarkLabel(endOfSerailizaion);
            }
        }

        private void GenerateILForObject(Type type, int depth)
        {
            // writer.WriteStartObject();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeStartObject);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
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
                Action pushPropertyValueOntoStack = () =>
                {
                    // obj.Property
                    that._ilg.Emit(OpCodes.Ldarg_0);
                    that._ilg.Emit(OpCodes.Ldfld, that.GetWriteStackFieldAtDepth(depth));
                    if (type.IsValueType)
                    {
                        that._ilg.Emit(OpCodes.Unbox, type);
                    }
                    that.GenerateILForCallingGetMethod(getMethod);
                };

                if (converter == null)
                {
                    GenerateILForNestedType(property.PropertyType, depth + 1, pushPropertyValueOntoStack);
                }
                else
                {
                    GenerateILForCallingConverter(property.PropertyType, pushPropertyValueOntoStack, converter);
                }
            }

            // writer.WriteEndObject();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeEndObject);
        }

        private void GenerateILForIEnumerable(Type currentType, int depth)
        {
            // First argument: Utf8JsonWriter writer
            // Second argument: IEnumerable<T> enumerable

            Type elementType = GetIEnumerableGenericType(currentType);
            Type enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
            Type enumeratorType = typeof(IEnumerator<>).MakeGenericType(elementType);

            // writer.WriteStartArray();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeStartArray);

            // _writeStackX = _writeStackX.GetEnumerator();
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldfld, GetWriteStackFieldAtDepth(depth));
            _ilg.Emit(OpCodes.Callvirt, enumerableType.GetMethod("GetEnumerator", new Type[] { }));
            _ilg.Emit(OpCodes.Stfld, GetWriteStackFieldAtDepth(depth));

            // while (_writeStackX.MoveNext()) {
            Label topOfIterationLoop = _ilg.DefineLabel();
            Label bottomOfIterationLoop = _ilg.DefineLabel();
            _ilg.MarkLabel(topOfIterationLoop);
            _ilg.Emit(OpCodes.Ldarg_0);
            _ilg.Emit(OpCodes.Ldfld, GetWriteStackFieldAtDepth(depth));
            // MoveNext is defined in IEnumerator
            _ilg.Emit(OpCodes.Callvirt, typeof(IEnumerator).GetMethod("MoveNext", new Type[] { }));
            _ilg.Emit(OpCodes.Brfalse, bottomOfIterationLoop);

            // Save converter to a global variable
            JsonConverter converter = _options.GetConverter(elementType);

            JitILCompiler that = this;
            Action pushNextElementOntoStack = () =>
            {
                // _writeStackX.Current
                that._ilg.Emit(OpCodes.Ldarg_0);
                that._ilg.Emit(OpCodes.Ldfld, that.GetWriteStackFieldAtDepth(depth));
                that._ilg.Emit(OpCodes.Callvirt, enumeratorType.GetProperty("Current").GetMethod);
            };

            if (converter == null)
            {
                GenerateILForNestedType(elementType, depth + 1, pushNextElementOntoStack);
            }
            else
            {
                GenerateILForCallingConverter(elementType, pushNextElementOntoStack, converter);
            }

            // }
            _ilg.Emit(OpCodes.Br, topOfIterationLoop);
            _ilg.MarkLabel(bottomOfIterationLoop);

            // writer.WriteEndArray();
            _ilg.Emit(OpCodes.Ldarg_1);
            _ilg.Emit(OpCodes.Call, _writeEndArray);
        }

        private void GenerateILForWritingPropertyName(PropertyInfo property)
        {
            JsonNamingPolicy policy = _options.PropertyNamingPolicy;
            JsonPropertyNameAttribute nameAttribute = (JsonPropertyNameAttribute)property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), inherit: false)
                    .FirstOrDefault();

            // writer.WritePropertyName("convertedName");
            _ilg.Emit(OpCodes.Ldarg_1);
            if (nameAttribute != null)
            {
                _ilg.Emit(OpCodes.Ldstr, nameAttribute.Name);
            }
            else if (policy == null)
            {
                _ilg.Emit(OpCodes.Ldstr, property.Name);
            }
            else
            {
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
            if (!_converterCache.TryGetValue(converter, out converterField))
            {
                string fieldName = @"Converter" + (++_converterCounter);
                converterField = _tb.DefineField(
                    fieldName: fieldName,
                    // Default converters are internal classes so here the abstract generic type must be used.
                    type: converterGenericType,
                    attributes: FieldAttributes.Public | FieldAttributes.Static
                );

                _converterCache.Add(converter, converterField);

                _followUp += type =>
                {
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

            MarkNewJumpTableLabel();
        }

        private void GenerateILForCallingGetMethod(MethodInfo getMethod)
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
            while (_writeStack.Count <= depth)
            {
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
