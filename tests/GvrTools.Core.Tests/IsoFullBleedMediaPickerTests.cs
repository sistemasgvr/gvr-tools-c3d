using System.Collections.Generic;
using GvrTools.Core.IO;
using Xunit;

namespace GvrTools.Core.Tests
{
    public sealed class IsoFullBleedMediaPickerTests
    {
        private static readonly List<string> SampleList = new List<string>
        {
            "ISO_expand_A4_(297.00_x_210.00_MM)",
            "ISO_full_bleed_A1_(594.00_x_841.00_MM)",
            "ISO_full_bleed_A1_(841.00_x_594.00_MM)",
            "ISO_full_bleed_A4_(210.00_x_297.00_MM)",
            "ISO_full_bleed_A4_(297.00_x_210.00_MM)",
            "Letter"
        };

        [Fact]
        public void Pick_A1_prefers_landscape()
        {
            string result = IsoFullBleedMediaPicker.Pick("A1", SampleList, preferLandscape: true);
            Assert.Equal("ISO_full_bleed_A1_(841.00_x_594.00_MM)", result);
        }

        [Fact]
        public void Pick_A1_can_prefer_portrait()
        {
            string result = IsoFullBleedMediaPicker.Pick("A1", SampleList, preferLandscape: false);
            Assert.Equal("ISO_full_bleed_A1_(594.00_x_841.00_MM)", result);
        }

        [Fact]
        public void Pick_A4_prefers_landscape()
        {
            string result = IsoFullBleedMediaPicker.Pick("A4", SampleList, preferLandscape: true);
            Assert.Equal("ISO_full_bleed_A4_(297.00_x_210.00_MM)", result);
        }

        [Fact]
        public void Pick_returns_null_when_size_missing()
        {
            Assert.Null(IsoFullBleedMediaPicker.Pick("A0", SampleList));
        }

        [Fact]
        public void HasAny_detects_full_bleed()
        {
            Assert.True(IsoFullBleedMediaPicker.HasAny(SampleList));
            Assert.False(IsoFullBleedMediaPicker.HasAny(new List<string> { "Letter", "ISO_A4_(210.00_x_297.00_MM)" }));
        }

        [Fact]
        public void IsLandscapeCanonical_parses_dimensions()
        {
            Assert.True(IsoFullBleedMediaPicker.IsLandscapeCanonical("ISO_full_bleed_A1_(841.00_x_594.00_MM)"));
            Assert.False(IsoFullBleedMediaPicker.IsLandscapeCanonical("ISO_full_bleed_A1_(594.00_x_841.00_MM)"));
        }
    }
}
