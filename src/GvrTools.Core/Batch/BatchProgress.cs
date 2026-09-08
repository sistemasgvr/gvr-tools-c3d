namespace GvrTools.Core.Batch
{
    /// <summary>Snapshot of how far a batch has got, reported once per item.</summary>
    public sealed class BatchProgress
    {
        public int Completed { get; }

        public int Total { get; }

        /// <summary>Label of the item currently being processed.</summary>
        public string CurrentLabel { get; }

        public BatchProgress(int completed, int total, string currentLabel)
        {
            Completed = completed;
            Total = total;
            CurrentLabel = currentLabel;
        }

        public double Fraction => Total <= 0 ? 0 : (double)Completed / Total;
    }
}
