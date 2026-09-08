using System;
using System.IO;
using GvrTools.Core.Settings;
using Xunit;

namespace GvrTools.Core.Tests
{
    public class FlatFileSettingsStoreTests : IDisposable
    {
        private enum Mode
        {
            First,
            Second
        }

        private sealed class Preferences
        {
            public string Folder { get; set; } = string.Empty;

            public bool Flag { get; set; } = true;

            public int Count { get; set; } = 7;

            public Mode Mode { get; set; } = Mode.First;
        }

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gvrtools-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Returns_defaults_when_nothing_is_stored()
        {
            var store = new FlatFileSettingsStore(_directory);

            Preferences loaded = store.Load<Preferences>("missing");

            Assert.Equal(string.Empty, loaded.Folder);
            Assert.True(loaded.Flag);
            Assert.Equal(7, loaded.Count);
            Assert.Equal(Mode.First, loaded.Mode);
        }

        [Fact]
        public void Round_trips_every_supported_property_type()
        {
            var store = new FlatFileSettingsStore(_directory);

            store.Save("prefs", new Preferences
            {
                Folder = @"D:\Exportaciones\Proyecto",
                Flag = false,
                Count = 42,
                Mode = Mode.Second
            });

            Preferences loaded = store.Load<Preferences>("prefs");

            Assert.Equal(@"D:\Exportaciones\Proyecto", loaded.Folder);
            Assert.False(loaded.Flag);
            Assert.Equal(42, loaded.Count);
            Assert.Equal(Mode.Second, loaded.Mode);
        }

        [Fact]
        public void A_corrupt_file_falls_back_to_defaults_instead_of_throwing()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "prefs.settings"), "esto no es\nun archivo de ajustes");

            Preferences loaded = new FlatFileSettingsStore(_directory).Load<Preferences>("prefs");

            Assert.Equal(7, loaded.Count);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            }
            catch (IOException)
            {
                // Temp folder cleanup is best effort.
            }
        }
    }
}
