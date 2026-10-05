using System.Collections.Generic;
using GvrTools.Core.IO;
using Xunit;

namespace GvrTools.Core.Tests
{
    public sealed class PlotOutputTests
    {
        [Theory]
        [InlineData("pdf", PlotOutputKind.Pdf)]
        [InlineData(".PDF", PlotOutputKind.Pdf)]
        [InlineData("dwf", PlotOutputKind.Dwf)]
        [InlineData("dwfx", PlotOutputKind.Dwf)]
        [InlineData("png", PlotOutputKind.Image)]
        [InlineData(".jpg", PlotOutputKind.Image)]
        [InlineData("plt", PlotOutputKind.PlotFile)]
        [InlineData("", PlotOutputKind.PlotFile)]
        public void Classify_by_extension_when_plotting_to_file(string extension, PlotOutputKind expected)
        {
            Assert.Equal(expected, PlotOutput.Classify(extension, plotsToFile: true));
        }

        [Fact]
        public void Classify_is_printer_when_not_plotting_to_file()
        {
            Assert.Equal(PlotOutputKind.Printer, PlotOutput.Classify("pdf", plotsToFile: false));
        }

        [Fact]
        public void Only_pdf_and_dwf_support_multi_sheet()
        {
            Assert.True(PlotOutput.SupportsMultiSheet(PlotOutputKind.Pdf));
            Assert.True(PlotOutput.SupportsMultiSheet(PlotOutputKind.Dwf));
            Assert.False(PlotOutput.SupportsMultiSheet(PlotOutputKind.Image));
            Assert.False(PlotOutput.SupportsMultiSheet(PlotOutputKind.Printer));
        }

        [Theory]
        [InlineData(PlotOutputKind.Pdf, "pdf", "PDF")]
        [InlineData(PlotOutputKind.Image, "png", "PNG")]
        [InlineData(PlotOutputKind.PlotFile, "plt", "Trazado")]
        public void Folder_prefix_follows_output(PlotOutputKind kind, string extension, string expected)
        {
            Assert.Equal(expected, PlotOutput.FolderPrefix(kind, extension));
        }

        [Theory]
        [InlineData("pdf", ".pdf")]
        [InlineData(" .dwf ", ".dwf")]
        [InlineData(null, ".plt")]
        public void Normalize_extension(string input, string expected)
        {
            Assert.Equal(expected, PlotOutput.NormalizeExtension(input));
        }
    }

    public sealed class PlotMediaNamesTests
    {
        [Fact]
        public void Humanize_turns_underscores_into_spaces()
        {
            Assert.Equal("ISO full bleed A1 (841.00 x 594.00 MM)",
                PlotMediaNames.Humanize("ISO_full_bleed_A1_(841.00_x_594.00_MM)"));
        }

        [Fact]
        public void Humanize_keeps_plain_names()
        {
            Assert.Equal("Letter", PlotMediaNames.Humanize("Letter"));
            Assert.Equal(string.Empty, PlotMediaNames.Humanize(null));
        }

        [Fact]
        public void Dimensions_are_read_from_canonical_names()
        {
            Assert.True(PlotMediaNames.TryGetDimensions("ANSI_A_(8.50_x_11.00_Inches)", out double w, out double h));
            Assert.Equal(8.5, w);
            Assert.Equal(11.0, h);
        }

        [Fact]
        public void Names_without_dimensions_are_rejected()
        {
            Assert.False(PlotMediaNames.TryGetDimensions("Letter", out _, out _));
        }
    }

    public sealed class PlotOrientationMathTests
    {
        [Theory]
        // Portrait media (ANSI A 8.5 x 11): landscape needs a quarter turn, like AutoCAD's default layout.
        [InlineData(true, false, false, 1)]
        [InlineData(false, false, false, 0)]
        [InlineData(true, true, false, 3)]
        // Landscape media (ISO full bleed A1 841 x 594): landscape is the media's own orientation.
        [InlineData(true, false, true, 0)]
        [InlineData(false, false, true, 1)]
        [InlineData(false, true, true, 3)]
        public void Quarter_turns_depend_on_media_orientation(bool landscape, bool upsideDown, bool mediaIsLandscape, int expected)
        {
            Assert.Equal(expected, PlotOrientationMath.ToQuarterTurns(landscape, upsideDown, mediaIsLandscape));
        }

        [Theory]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, true)]
        [InlineData(false, false, true)]
        public void Round_trip(bool landscape, bool upsideDown, bool mediaIsLandscape)
        {
            int turns = PlotOrientationMath.ToQuarterTurns(landscape, upsideDown, mediaIsLandscape);
            PlotOrientationMath.FromQuarterTurns(turns, mediaIsLandscape, out bool backLandscape, out bool backUpsideDown);

            Assert.Equal(landscape, backLandscape);
            Assert.Equal(upsideDown, backUpsideDown);
        }
    }

    public sealed class PlotScaleTests
    {
        [Fact]
        public void Format_name_trims_decimals()
        {
            Assert.Equal("1:100", PlotScale.FormatName(1, 100));
            Assert.Equal("1:2.5", PlotScale.FormatName(1, 2.5));
        }

        [Fact]
        public void Find_matches_by_ratio()
        {
            PlotScale found = PlotScaleList.Find(PlotScaleList.Default, 10, 1000);
            Assert.NotNull(found);
            Assert.Equal("1:100", found.Name);
        }

        [Fact]
        public void Normalize_drops_duplicates_and_invalid_entries()
        {
            var input = new List<PlotScale>
            {
                new PlotScale("1:50", 1, 50),
                new PlotScale("2:100", 2, 100),
                new PlotScale("bad", 0, 1),
                new PlotScale("1:200", 1, 200)
            };

            IReadOnlyList<PlotScale> result = PlotScaleList.Normalize(input);

            Assert.Equal(2, result.Count);
            Assert.Equal("1:50", result[0].Name);
            Assert.Equal("1:200", result[1].Name);
        }

        [Fact]
        public void Normalize_falls_back_to_default_list()
        {
            Assert.Same(PlotScaleList.Default, PlotScaleList.Normalize(null));
        }

        [Fact]
        public void Normalize_prefers_clean_names_over_xref_copies()
        {
            var input = new List<PlotScale>
            {
                new PlotScale("1:1_1", 1, 1),
                new PlotScale("1:100_XREF", 1, 100),
                new PlotScale("1:1", 1, 1),
                new PlotScale("1:100", 1, 100)
            };

            IReadOnlyList<PlotScale> result = PlotScaleList.Normalize(input);

            Assert.Equal(new[] { "1:1", "1:100" }, new[] { result[0].Name, result[1].Name });
        }
    }

    public sealed class PlotMediaFallbackTests
    {
        [Fact]
        public void Same_physical_size_under_another_name_is_used()
        {
            var list = new List<string> { "Letter", "ISO_A1_(594.00_x_841.00_MM)", "ISO_A1_(841.00_x_594.00_MM)" };
            Assert.Equal("ISO_A1_(841.00_x_594.00_MM)",
                PlotMediaResolver.Resolve("ISO_full_bleed_A1_(841.00_x_594.00_MM)", list, "Letter"));
        }

        [Fact]
        public void Other_orientation_is_accepted_when_it_is_the_only_match()
        {
            var list = new List<string> { "Letter", "ISO_A1_(594.00_x_841.00_MM)" };
            Assert.Equal("ISO_A1_(594.00_x_841.00_MM)",
                PlotMediaResolver.Resolve("ISO_full_bleed_A1_(841.00_x_594.00_MM)", list, "Letter"));
        }

        [Fact]
        public void Device_default_beats_first_entry_for_pixel_devices()
        {
            var list = new List<string> { "16K_(15360.00_x_8640.00_Pixels)", "Sun_Hi-Res_(1600.00_x_1280.00_Pixels)" };
            Assert.Equal("Sun_Hi-Res_(1600.00_x_1280.00_Pixels)",
                PlotMediaResolver.Resolve("ISO_full_bleed_A1_(841.00_x_594.00_MM)", list, "Sun_Hi-Res_(1600.00_x_1280.00_Pixels)"));
        }
    }

    public sealed class PlotPaperFitTests
    {
        [Fact]
        public void A1_sheet_on_A4_at_1to1_clips()
        {
            PlotPaperFit.TryGetIsoSize("A4", out double w, out double h);
            Assert.True(PlotPaperFit.WouldClip(841, 594, w, h, scaleRatio: 1.0));
        }

        [Fact]
        public void A1_sheet_on_A4_at_1to4_fits()
        {
            Assert.False(PlotPaperFit.WouldClip(841, 594, 297, 210, scaleRatio: 0.25));
        }

        [Fact]
        public void Same_sheet_in_other_orientation_fits()
        {
            Assert.False(PlotPaperFit.WouldClip(594, 841, 841, 594, scaleRatio: 1.0));
        }

        [Fact]
        public void Inch_media_is_converted_to_millimeters()
        {
            Assert.True(PlotMediaNames.TryGetSizeInMillimeters("ANSI_A_(8.50_x_11.00_Inches)", out double w, out double h));
            Assert.Equal(215.9, w, 1);
            Assert.Equal(279.4, h, 1);
        }

        [Fact]
        public void Pixel_media_has_no_physical_size()
        {
            Assert.False(PlotMediaNames.TryGetSizeInMillimeters("Sun_Hi-Res_(1600.00_x_1280.00_Pixels)", out _, out _));
        }
    }
}
