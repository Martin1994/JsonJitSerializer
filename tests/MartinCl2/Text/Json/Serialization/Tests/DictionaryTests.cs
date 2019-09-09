using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    public class DictionaryTests
    {
        [Fact]
        public async Task BasicDictionaryTest()
        {
            Dictionary<string, int> payload = new Dictionary<string, int>()
            {
                { "One", 1 },
                { "Two", 2 },
                { "Three", 3 }
            };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task NamingPolicyDictionaryTest()
        {
            Dictionary<string, int> payload = new Dictionary<string, int>()
            {
                { "One", 1 },
                { "Two", 2 },
                { "Three", 3 }
            };

            JsonSerializerOptions options = new JsonSerializerOptions()
            {
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
            };

            await TestUtil.AssertJsonIsIdentical(payload, options);
        }
    }
}
