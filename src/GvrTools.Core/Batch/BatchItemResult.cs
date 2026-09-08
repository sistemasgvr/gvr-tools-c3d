namespace GvrTools.Core.Batch
{
    public enum BatchItemStatus
    {
        Succeeded,
        Failed,
        Skipped
    }

    /// <summary>Outcome of one item in a batch operation (one sheet, one file, one element...).</summary>
    public sealed class BatchItemResult
    {
        public string Label { get; }

        public BatchItemStatus Status { get; }

        /// <summary>Where the produced file landed, when the item produced one.</summary>
        public string OutputPath { get; }

        /// <summary>User-facing explanation. Always present for failures.</summary>
        public string Message { get; }

        private BatchItemResult(string label, BatchItemStatus status, string outputPath, string message)
        {
            Label = label;
            Status = status;
            OutputPath = outputPath;
            Message = message;
        }

        public bool Succeeded => Status == BatchItemStatus.Succeeded;

        public static BatchItemResult Success(string label, string outputPath) =>
            new BatchItemResult(label, BatchItemStatus.Succeeded, outputPath, null);

        public static BatchItemResult Failure(string label, string message) =>
            new BatchItemResult(label, BatchItemStatus.Failed, null, message);

        public static BatchItemResult Skipped(string label, string message) =>
            new BatchItemResult(label, BatchItemStatus.Skipped, null, message);
    }
}
