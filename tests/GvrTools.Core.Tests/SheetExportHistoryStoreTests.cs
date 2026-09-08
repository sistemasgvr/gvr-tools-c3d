using System;
using System.Collections.Generic;
using System.IO;
using GvrTools.Core.History;
using Xunit;

namespace GvrTools.Core.Tests
{
    public class SheetExportHistoryStoreTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "gvrtools-tests-" + Guid.NewGuid().ToString("N"));

        [Fact]
        public void Returns_empty_when_nothing_is_stored()
        {
            var store = new SheetExportHistoryStore(_directory);

            IReadOnlyDictionary<string, DateTime> loaded = store.Load("missing-project");

            Assert.Empty(loaded);
        }

        [Fact]
        public void Round_trips_sheet_timestamps()
        {
            var store = new SheetExportHistoryStore(_directory);
            var when = new DateTime(2026, 8, 26, 10, 30, 0, DateTimeKind.Utc);

            store.Save("proj-1", new Dictionary<string, DateTime>
            {
                ["sheet-guid-a"] = when,
                ["sheet-guid-b"] = when.AddDays(-3)
            });

            IReadOnlyDictionary<string, DateTime> loaded = store.Load("proj-1");

            Assert.Equal(when, loaded["sheet-guid-a"]);
            Assert.Equal(when.AddDays(-3), loaded["sheet-guid-b"]);
        }

        [Fact]
        public void Different_projects_do_not_share_history()
        {
            var store = new SheetExportHistoryStore(_directory);
            var when = DateTime.UtcNow;

            store.Save("proj-a", new Dictionary<string, DateTime> { ["sheet-1"] = when });
            store.Save("proj-b", new Dictionary<string, DateTime>());

            Assert.Single(store.Load("proj-a"));
            Assert.Empty(store.Load("proj-b"));
        }

        [Fact]
        public void A_corrupt_file_falls_back_to_empty_instead_of_throwing()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "proj-1.history"), "esto no es\nun historial válido");

            IReadOnlyDictionary<string, DateTime> loaded = new SheetExportHistoryStore(_directory).Load("proj-1");

            Assert.Empty(loaded);
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
