using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MartinCl2.Text.Json.Serialization;

namespace JsonJIT
{
    public class Program
    {
        public class Nested<T>
        {
            private readonly T payload;
            public T First { get => payload; }
            public T Second { get => payload; }
            public T Third { get => payload; }

            public Nested(T payload) {
                this.payload = payload;
            }
        }

        public class Address
        {
            public uint number { get; set; }
            public string street { get; set; }
        }

        [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
        private class AddressConverterAttribute : JsonConverterAttribute
        {
            public override JsonConverter CreateConverter(Type typeToConvert)
            {
                return new AddressConverter();
            }
        }

        public class AddressConverter : JsonConverter<Address>
        {
            public override Address Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                throw new NotImplementedException();
            }

            public override void Write(Utf8JsonWriter writer, Address value, JsonSerializerOptions options)
            {
                writer.WriteStringValue(String.Format("{0} {1}", value.number, value.street));
            }
        }

        public class TestPoco
        {
            public string A { get; set; }

            public DateTime B { get; set; }

            public int C { get; set; }

            public bool[] D { get; set; }

            public virtual string Name { get => "TestPoco"; }
        }

        public class TestPocoSub : TestPoco
        {

            public double SubA { get; set; }

            [AddressConverterAttribute]
            public Address SubB { get; set; }

            public override string Name { get => "TestPocoSub"; }
        }

        static void Main(string[] args)
        {
            TestPoco poco = new TestPoco(){
                A = "test"
            };

            Nested<Nested<TestPoco>> obj = new Nested<Nested<TestPoco>>(new Nested<TestPoco>(poco));

            Console.WriteLine(CompileAndSerialize(obj));

            TestPocoSub pocoSub = new TestPocoSub(){
                A = "123",
                B = DateTime.Now,
                C = 7,
                D = new bool[] { false, false, true, false, true, false, true, false },
                SubA = Math.PI,
                SubB = new Address() {
                    number = 120,
                    street = "Bremner Blvd."
                }
            };

            Console.WriteLine(CompileAndSerialize(pocoSub));

            Console.WriteLine(CompileAndSerialize((TestPoco)pocoSub));
        }

        static string CompileAndSerialize<T>(T obj)
        {
            var serailizer = JsonJitSerializer.Compile<T>(new JsonSerializerOptions() {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return serailizer.Serialize(obj);
        }
    }
}
