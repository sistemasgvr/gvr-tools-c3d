using GvrTools.Core.Batch;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>One row of the post-export results list.</summary>
    public sealed class ExportResultItemViewModel
    {
        public ExportResultItemViewModel(BatchItemResult result)
        {
            Label = result.Label;
            Succeeded = result.Succeeded;
            Detail = result.Succeeded ? result.OutputPath : result.Message;
        }

        public string Label { get; }

        public bool Succeeded { get; }

        public string Detail { get; }

        public string StatusIcon => Succeeded ? "✓" : "✕";
    }
}
