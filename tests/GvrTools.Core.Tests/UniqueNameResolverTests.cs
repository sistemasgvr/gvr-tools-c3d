using System;
using System.Collections.Generic;
using GvrTools.Core.Naming;
using Xunit;

namespace GvrTools.Core.Tests
{
    public class UniqueNameResolverTests
    {
        [Fact]
        public void Returns_the_name_unchanged_when_it_is_free()
        {
            var resolver = new UniqueNameResolver(@"C:\out", _ => false);

            Assert.Equal("A-101", resolver.ReserveBaseName("A-101", ".pdf"));
        }

        [Fact]
        public void Suffixes_a_name_that_already_exists_on_disk()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\out\A-101.pdf" };
            var resolver = new UniqueNameResolver(@"C:\out", existing.Contains);

            Assert.Equal("A-101_2", resolver.ReserveBaseName("A-101", ".pdf"));
        }

        [Fact]
        public void Suffixes_a_name_already_handed_out_in_this_run()
        {
            var resolver = new UniqueNameResolver(@"C:\out", _ => false);

            Assert.Equal("A-101", resolver.ReserveBaseName("A-101", ".pdf"));
            Assert.Equal("A-101_2", resolver.ReserveBaseName("A-101", ".pdf"));
            Assert.Equal("A-101_3", resolver.ReserveBaseName("A-101", ".pdf"));
        }

        [Fact]
        public void Reservations_are_per_extension()
        {
            var resolver = new UniqueNameResolver(@"C:\out", _ => false);

            Assert.Equal("A-101", resolver.ReserveBaseName("A-101", ".pdf"));
            Assert.Equal("A-101", resolver.ReserveBaseName("A-101", ".dwg"));
        }

        [Fact]
        public void Builds_a_full_path()
        {
            var resolver = new UniqueNameResolver(@"C:\out", _ => false);

            Assert.Equal(@"C:\out\A-101.pdf", resolver.ReservePath("A-101", "pdf"));
        }

        [Fact]
        public void Avoids_revit_dwg_view_suffix_collisions()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\out\E101-Autodesk Logo.dwg"
            };
            var resolver = new UniqueNameResolver(@"C:\out", existing.Contains);

            Assert.Equal("E101_2", resolver.ReserveBaseName("E101", ".dwg", new[] { "Autodesk Logo" }));
        }
    }
}
