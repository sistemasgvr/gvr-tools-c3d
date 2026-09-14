using System.Collections.Generic;
using GvrTools.Core.IO;
using Xunit;

namespace GvrTools.Core.Tests
{
    public sealed class PlotMediaResolverTests
    {
        [Fact]
        public void Resolve_keeps_desired_when_present()
        {
            var list = new List<string> { "ISO_full_bleed_A1_(841.00_x_594.00_MM)", "ISO_A4_(210.00_x_297.00_MM)" };

            string result = PlotMediaResolver.Resolve("ISO_full_bleed_A1_(841.00_x_594.00_MM)", list);

            Assert.Equal("ISO_full_bleed_A1_(841.00_x_594.00_MM)", result);
        }

        [Fact]
        public void Resolve_is_case_insensitive_for_desired()
        {
            var list = new List<string> { "ISO_A4_(210.00_x_297.00_MM)" };

            string result = PlotMediaResolver.Resolve("iso_a4_(210.00_x_297.00_mm)", list);

            Assert.Equal("ISO_A4_(210.00_x_297.00_MM)", result);
        }

        [Fact]
        public void Resolve_falls_back_to_preferred_full_bleed()
        {
            var list = new List<string>
            {
                "Letter",
                "ISO_full_bleed_A3_(420.00_x_297.00_MM)",
                "ANSI_A_(8.50_x_11.00_Inches)"
            };

            string result = PlotMediaResolver.Resolve("Missing_Size", list);

            Assert.Equal("ISO_full_bleed_A3_(420.00_x_297.00_MM)", result);
        }

        [Fact]
        public void Resolve_returns_null_for_empty_list()
        {
            Assert.Null(PlotMediaResolver.Resolve("ISO_A4_", new List<string>()));
            Assert.Null(PlotMediaResolver.Resolve("ISO_A4_", null));
        }

        [Fact]
        public void Resolve_returns_first_when_no_preference_matches()
        {
            var list = new List<string> { "Custom_Weird_Size", "Another_Custom" };

            string result = PlotMediaResolver.Resolve(null, list);

            Assert.Equal("Custom_Weird_Size", result);
        }

        [Fact]
        public void FindExact_returns_match_case_insensitive()
        {
            var list = new List<string> { "ISO_full_bleed_A4_(297.00_x_210.00_MM)", "Letter" };

            Assert.Equal(
                "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
                PlotMediaResolver.FindExact("iso_full_bleed_a4_(297.00_x_210.00_mm)", list));
        }

        [Fact]
        public void FindExact_returns_null_when_missing()
        {
            var list = new List<string> { "Letter" };
            Assert.Null(PlotMediaResolver.FindExact("ISO_full_bleed_A4_(297.00_x_210.00_MM)", list));
            Assert.Null(PlotMediaResolver.FindExact("", list));
            Assert.Null(PlotMediaResolver.FindExact("Letter", null));
        }
    }
}
