using System;
using System.Text.Json;
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

        public class TestPoco
        {
            public string A { get; set; }
            public DateTime B { get; set; }
            public int C { get; set; }
        }

        static void Main(string[] args)
        {
            TestPoco poco = new TestPoco(){
                A = "123",
                B = DateTime.Now,
                C = 7
            };

            Nested<Nested<TestPoco>> obj = new Nested<Nested<TestPoco>>(new Nested<TestPoco>(poco));
            Console.WriteLine(JsonJitSerializer.Serialize(obj, new JsonSerializerOptions() {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
        }
    }
}
