using System.Collections.Generic;
using GvrTools.Core.Naming;
using Xunit;

namespace GvrTools.Core.Tests
{
    public class FileNameBuilderTests
    {
        private static readonly Dictionary<string, string> Sheet = new Dictionary<string, string>
        {
            ["SheetNumber"] = "A-101",
            ["SheetName"] = "Planta primer nivel",
            ["RevisionNumber"] = string.Empty
        };

        [Fact]
        public void Expands_known_tokens()
        {
            string name = FileNameBuilder.Build("{SheetNumber} - {SheetName}", Sheet, "fallback");

            Assert.Equal("A-101 - Planta primer nivel", name);
        }

        [Fact]
        public void Drops_the_separator_of_an_empty_token()
        {
            string name = FileNameBuilder.Build("{SheetNumber} - {RevisionNumber}", Sheet, "fallback");

            Assert.Equal("A-101", name);
        }

        [Fact]
        public void Unknown_tokens_expand_to_nothing()
        {
            string name = FileNameBuilder.Build("{SheetNumber} - {NoSuchToken}", Sheet, "fallback");

            Assert.Equal("A-101", name);
        }

        [Fact]
        public void Replaces_characters_windows_rejects()
        {
            var tokens = new Dictionary<string, string> { ["SheetName"] = "Corte A/B: detalle" };

            string name = FileNameBuilder.Build("{SheetName}", tokens, "fallback");

            Assert.Equal("Corte A_B_ detalle", name);
        }

        [Fact]
        public void Falls_back_when_the_pattern_yields_nothing()
        {
            string name = FileNameBuilder.Build("{NoSuchToken}", Sheet, "A-101");

            Assert.Equal("A-101", name);
        }

        [Fact]
        public void Leaves_unclosed_braces_alone()
        {
            string name = FileNameBuilder.Build("{SheetNumber} {Sheet", Sheet, "fallback");

            Assert.Equal("A-101 {Sheet", name);
        }
    }
}
