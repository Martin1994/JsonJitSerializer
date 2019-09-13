using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace MartinCl2.Text.Json.Serialization.Tests
{
    public class EnumerableTests
    {
        [Fact]
        public async Task ArrayTest()
        {
            int[] payload = new int[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task ValueTypeEnumeratorTest()
        {
            HashSet<int> payload = new HashSet<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task ListTest()
        {
            List<int> payload = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task IEnumerableTest()
        {
            IEnumerable<int> payload = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task IListTest()
        {
            IList<int> payload = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        [Fact]
        public async Task IReadOnlyListTest()
        {
            IReadOnlyList<int> payload = new List<int>() { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            await TestUtil.AssertJsonIsIdentical(payload);
        }

        public class IEnumerableTestPayload : IEnumerable<int>
        {
            private IEnumerable<int> Generate()
            {
                yield return 0;
                yield return 1;
                yield return 2;
                yield return 3;
                yield return 4;
                yield return 5;
                yield return 6;
                yield return 7;
                yield return 8;
                yield return 9;
            }
            IEnumerator<int> IEnumerable<int>.GetEnumerator()
            {
                IEnumerable<int> generator = Generate();
                return generator.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                IEnumerable<int> generator = Generate();
                return generator.GetEnumerator();
            }
        }

        [Fact]
        public async Task CustomEnumerableTest() => await TestUtil.TestSerializationWithDefaultProperties<IEnumerableTestPayload>();
    }
}
