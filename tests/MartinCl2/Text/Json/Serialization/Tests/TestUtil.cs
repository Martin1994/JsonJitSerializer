using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    public static class TestUtil
    {
        public static async Task AssertJsonIsIdentical<T>(T payload, JsonSerializerOptions options = null)
        {
            JsonJitSerializer<T> serializer = JsonJitSerializer<T>.Compile(options);

            string actual = serializer.Serialize(payload);
            string actualAsync = await serializer.SerializeAsync(payload);
            string expected = JsonSerializer.Serialize(payload, options);

            Assert.Equal(expected, actual);
            Assert.Equal(expected, actualAsync);
        }

        public static Task TestSerializationWithDefaultProperties<T>() where T: new()
        {
            T payload = new T();

            return TestUtil.AssertJsonIsIdentical(payload);
        }

        public static async Task<string> SerializeAsync<T>(this JsonJitSerializer<T> serializer, T obj)
        {
            MemoryStream stream = new MemoryStream();
            using (Utf8JsonWriter writer = new Utf8JsonWriter(stream))
            {
                await serializer.SerializeAsync(writer, obj);
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
