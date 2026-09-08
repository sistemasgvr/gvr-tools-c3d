using System;
using System.Collections.Generic;
using System.Linq;

namespace GvrTools.Core.Batch
{
    /// <summary>Final report of a batch operation.</summary>
    public sealed class BatchResult
    {
        public IReadOnlyList<BatchItemResult> Items { get; }

        public bool WasCancelled { get; }

        public string DestinationFolder { get; }

        public TimeSpan Elapsed { get; }

        /// <summary>
        /// Set when the run never got off the ground (bad printer, unwritable folder...). The
        /// message is meant to be shown to the user as-is.
        /// </summary>
        public string SetupError { get; }

        public BatchResult(
            IReadOnlyList<BatchItemResult> items,
            bool wasCancelled,
            string destinationFolder,
            TimeSpan elapsed,
            string setupError = null)
        {
            Items = items ?? new List<BatchItemResult>();
            WasCancelled = wasCancelled;
            DestinationFolder = destinationFolder;
            Elapsed = elapsed;
            SetupError = setupError;
        }

        public int SucceededCount => Items.Count(i => i.Status == BatchItemStatus.Succeeded);

        public int FailedCount => Items.Count(i => i.Status == BatchItemStatus.Failed);

        public IEnumerable<BatchItemResult> Failures => Items.Where(i => i.Status == BatchItemStatus.Failed);

        public bool HasSetupError => !string.IsNullOrEmpty(SetupError);
    }
}
