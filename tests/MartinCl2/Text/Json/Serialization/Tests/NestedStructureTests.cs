using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    public class NestedStructureTests
    {
        public class SimpleReferencePayload
        {
            private static readonly Object _emptyObject = new Object();
            public Object Property { get => _emptyObject; }
        }

        public struct SimpleValueTypePayload
        {
            public int Property { get => 1; }
        }

        public struct SimpleNestedValueTypePayload
        {
            private static readonly SimpleValueTypePayload _propertyValue = new SimpleValueTypePayload();
            public SimpleValueTypePayload Property { get => _propertyValue; }
        }

        public class BasicNestedStructureTestPayload
        {
            public int Convertee { get => 1; }
            private static readonly SimpleNestedValueTypePayload _nestedValueType = new SimpleNestedValueTypePayload();
            public SimpleNestedValueTypePayload NestedValueType { get => _nestedValueType; }
            private static readonly SimpleValueTypePayload _plainValueType = new SimpleValueTypePayload();
            public SimpleValueTypePayload PlainValueType { get => _plainValueType; }
            private static readonly SimpleReferencePayload _plainRefrenceType = new SimpleReferencePayload();
            public SimpleReferencePayload PlainReferenceType { get => _plainRefrenceType; }
            private static readonly List<SimpleReferencePayload> _nestedEnumerable = new List<SimpleReferencePayload>()
            {
                new SimpleReferencePayload(),
                new SimpleReferencePayload()
            };
            public List<SimpleReferencePayload> NestedEnumerable { get => _nestedEnumerable; }
            private static readonly Dictionary<string, SimpleReferencePayload> _nestedDictionary = new Dictionary<string, SimpleReferencePayload>()
            {
                { "first", new SimpleReferencePayload() },
                { "second", new SimpleReferencePayload() }
            };
            public Dictionary<string, SimpleReferencePayload> NestedDictionary { get => _nestedDictionary; }
        }

        [Fact]
        public async Task BasicNestedStructureTest() => await TestUtil.TestSerializationWithDefaultProperties<BasicNestedStructureTestPayload>();

        [Fact]
        public async Task EnumerableOfReferenceTypeTest()
        {
            SimpleReferencePayload[] payload = new SimpleReferencePayload[]
            {
                new SimpleReferencePayload(),
                null,
                new SimpleReferencePayload()
            };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task EnumerableOfValueTypeTest()
        {
            SimpleNestedValueTypePayload[] payload = new SimpleNestedValueTypePayload[3];

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task DictionaryOfReferenceTypeTest()
        {
            Dictionary<string, SimpleReferencePayload> payload = new Dictionary<string, SimpleReferencePayload>()
            {
                { "1", new SimpleReferencePayload() },
                // System.Test.Json does not handle this case
                // { "2", null },
                { "3", new SimpleReferencePayload() }
            };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task DictionaryOfValueTypeTest()
        {
            Dictionary<string, SimpleNestedValueTypePayload> payload = new Dictionary<string, SimpleNestedValueTypePayload>()
            {
                { "1", new SimpleNestedValueTypePayload() },
                { "2", new SimpleNestedValueTypePayload() },
                { "3", new SimpleNestedValueTypePayload() }
            };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        public class RefPropertyTestPayload
        {
            private static SimpleValueTypePayload _plainValueType = new SimpleValueTypePayload();
            public ref readonly SimpleValueTypePayload ReadonlyPlainValueType { get => ref _plainValueType; }
            public ref SimpleValueTypePayload PlainValueType { get => ref _plainValueType; }
            private static SimpleReferencePayload _plainRefrenceType = new SimpleReferencePayload();
            public ref readonly SimpleReferencePayload ReadonlyPlainReferenceType { get => ref _plainRefrenceType; }
            public ref SimpleReferencePayload PlainReferenceType { get => ref _plainRefrenceType; }
        }

        [Fact]
        public async Task RefPropertyTest()
        {
            // Unfortunately System.Test.Json does not support ref type for now. So we have to manually test it.
            RefPropertyTestPayload payload = new RefPropertyTestPayload();
            JsonJitSerializer<RefPropertyTestPayload> serializer = JsonJitSerializer<RefPropertyTestPayload>.Compile();

            string actual = serializer.Serialize(payload);
            string actualAsync = await serializer.SerializeAsync(payload);
            string expected = "{\"ReadonlyPlainValueType\":{\"Property\":1},\"PlainValueType\":{\"Property\":1},\"ReadonlyPlainReferenceType\":{\"Property\":{}},\"PlainReferenceType\":{\"Property\":{}}}";

            Assert.Equal(expected, actual);
            Assert.Equal(expected, actualAsync);
        }
    }
}
