using System.Collections.Generic;
using System.Linq;
using GvrTools.Core.Text;
using Xunit;

namespace GvrTools.Core.Tests
{
    public class NaturalSortComparerTests
    {
        [Fact]
        public void Orders_sheet_numbers_the_way_a_person_would()
        {
            var input = new List<string> { "A-10", "A-2", "A-1", "A-100", "A-20" };

            List<string> sorted = input.OrderBy(value => value, NaturalSortComparer.Instance).ToList();

            Assert.Equal(new[] { "A-1", "A-2", "A-10", "A-20", "A-100" }, sorted);
        }

        [Fact]
        public void Groups_by_prefix_before_number()
        {
            var input = new List<string> { "E-1", "A-2", "A-1", "E-10" };

            List<string> sorted = input.OrderBy(value => value, NaturalSortComparer.Instance).ToList();

            Assert.Equal(new[] { "A-1", "A-2", "E-1", "E-10" }, sorted);
        }

        [Fact]
        public void Ignores_leading_zeros_in_numbers()
        {
            Assert.Equal(0, NaturalSortComparer.Instance.Compare("A-007", "A-7"));
        }

        [Fact]
        public void Handles_nulls()
        {
            Assert.True(NaturalSortComparer.Instance.Compare(null, "A-1") < 0);
            Assert.True(NaturalSortComparer.Instance.Compare("A-1", null) > 0);
            Assert.Equal(0, NaturalSortComparer.Instance.Compare(null, null));
        }
    }
}
