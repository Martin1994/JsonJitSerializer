using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    public class PlainObjectTests
    {
        public class BasicObjectTestPayload
        {
            public int PropertyA { get => 1; }
            public int PropertyB { get => 2; }
            public int PropertyC { get => 3; }
            private int Private { get => throw new InvalidOperationException(); }
            protected int Protected { get => throw new InvalidOperationException(); }
            internal int Internal { get => throw new InvalidOperationException(); }
            public static int Static { get => throw new InvalidOperationException(); }
        }

        [Fact]
        public async Task BasicObjectTest() => await TestUtil.TestSerializationWithDefaultProperties<BasicObjectTestPayload>();

        [Fact]
        public async Task NullRootObjectTest() => await TestUtil.AssertJsonIsIdentical<Object>(null);

        public struct BasicStructTestPayload
        {
            public int PropertyA { get => 1; }
            public int PropertyB { get => 2; }
            public int PropertyC { get => 3; }
        }

        [Fact]
        public async Task BasicStructTest() => await TestUtil.TestSerializationWithDefaultProperties<BasicStructTestPayload>();

        public class NamingPolicyObjectTestPayload
        {
            public int camelCase { get => 1; }
            public int PascalCase { get => 2; }
            public int snake_case { get => 3; }
            public int UPPER_SNAKE_CASE { get => 4; }
            [JsonPropertyName("overriden-name")]
            public int CustomName { get => 5; }
        }

        [Fact]
        public async Task NamingPolicyObjectTest()
        {
            NamingPolicyObjectTestPayload payload = new NamingPolicyObjectTestPayload();

            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            await TestUtil.AssertJsonIsIdentical(payload, options);
        }

        public abstract class VirtualPropertyTestPayloadBase
        {
            public int NonVirtual { get => 1; }
            public virtual int VirtualWithOverride { get => 2; }
            public virtual int VirtualWithoutOverride { get => 3; }
            public abstract int Abstract { get; }
            public int New { get => 4; }
        }

        public class VirtualPropertyTestPayload : VirtualPropertyTestPayloadBase
        {
            public override int VirtualWithOverride { get => -2; }
            public override int Abstract { get => 0; }
            public new int New { get => -4; }
        }

        [Fact]
        public async Task VirtualPropertyOnChildTest() => await TestUtil.TestSerializationWithDefaultProperties<VirtualPropertyTestPayload>();

        [Fact]
        public async Task VirtualPropertyOnParentTest()
        {
            VirtualPropertyTestPayloadBase payload = new VirtualPropertyTestPayload();

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        public class NullablePropertyTestPayload
        {
            public int ValueTypeZeroProperty { get => 0; }
            public int ValueTypeNonZeroProperty { get => 1; }
            public string ReferenceTypeNullProperty { get => null; }
            public string ReferenceTypeNonNullProperty { get => ""; }
        }

        [Fact]
        public async Task NullablePropertyTest() => await TestUtil.TestSerializationWithDefaultProperties<NullablePropertyTestPayload>();

        public class RefPropertyTestPayload
        {
            private int _valueType = 1;
            public ref int ValueType { get => ref _valueType; }
            private string _referenceType = "2";
            public ref string ReferenceType { get => ref _referenceType; }
        }

        [Fact]
        public async Task RefPropertyTest()
        {
            // Unfortunately System.Test.Json does not support ref type for now. So we have to manually test it.
            RefPropertyTestPayload payload = new RefPropertyTestPayload();
            JsonJitSerializer<RefPropertyTestPayload> serializer = JsonJitSerializer<RefPropertyTestPayload>.Compile();

            string actual = serializer.Serialize(payload);
            string actualAsync = await serializer.SerializeAsync(payload);
            string expected = "{\"ValueType\":1,\"ReferenceType\":\"2\"}";

            Assert.Equal(expected, actual);
            Assert.Equal(expected, actualAsync);
        }

        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
        private class EnglishNumberConverterAttribute : JsonConverterAttribute
        {
            public override JsonConverter CreateConverter(Type typeToConvert)
            {
                return new EnglishNumberConverter();
            }
        }

        public class EnglishNumberConverter : JsonConverter<int>
        {
            public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
            {
                if (value == 1)
                {
                    writer.WriteStringValue(String.Format("one"));
                }
                else
                {
                    throw new NotImplementedException();
                }
            }
        }

        public class ConverterTestPayload
        {
            [EnglishNumberConverter]
            public int CustomComverter { get => 1; }
            public int Integer { get => -2; }
            public uint UInteger { get => 2; }
            public long Long { get => -3L; }
            public ulong ULong { get => 3L; }
            public float Float { get => 4.0f; }
            public double Double { get => 5.0d; }
            public decimal Decimal { get => 6.0M; }
            public byte Byte { get => 7; }
            public short Short { get => -8; }
            public ushort UShort { get => 8; }
            public bool Bool { get => true; }
            private DateTime _dateTime = DateTime.Now;
            public DateTime DateTime { get => _dateTime; }
            private DateTimeOffset _dateTimeOffset = DateTimeOffset.Now;
            public DateTimeOffset DateTimeOffset { get => _dateTimeOffset; }
            public string String { get => "string"; }
            public char Char { get => 'c'; }
            public Guid Guid { get => new Guid(); }
            public Uri Uri { get => new Uri("http://127.0.0.1:8080"); }
        }

        [Fact]
        public async Task ConverterTest() => await TestUtil.TestSerializationWithDefaultProperties<ConverterTestPayload>();
    }
}
