using System;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    internal class InvalidParameterTestsInternalClass { }

    public class InvalidParameterTests
    {
        private class PrivateNestedClass { }

        [Fact]
        public async Task PrivateNestedTypeTest()
        {
            PrivateNestedClass payload = new PrivateNestedClass();

            await Assert.ThrowsAsync<TypeAccessException>(() => TestUtil.AssertJsonIsIdentical(payload));
        }

        [Fact]
        public async Task InternalTypeTest()
        {
            InvalidParameterTestsInternalClass payload = new InvalidParameterTestsInternalClass();

            await Assert.ThrowsAsync<TypeAccessException>(() => TestUtil.AssertJsonIsIdentical(payload));
        }
    }
}
