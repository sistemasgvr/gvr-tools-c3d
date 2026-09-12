using GvrTools.Core.Batch;

namespace GvrTools.Tools.BatchExport.ViewModels
{
    /// <summary>One row of the post-export results list.</summary>
    public sealed class ExportResultItemViewModel
    {
        private readonly BatchItemStatus _status;

        public ExportResultItemViewModel(BatchItemResult result)
        {
            Label = result.Label;
            _status = result.Status;
            Succeeded = result.Succeeded;
            Detail = result.Succeeded ? result.OutputPath : result.Message;
        }

        public string Label { get; }

        public bool Succeeded { get; }

        public string Detail { get; }

        public string StatusIcon
        {
            get
            {
                switch (_status)
                {
                    case BatchItemStatus.Succeeded: return "✓";
                    case BatchItemStatus.Skipped: return "○";
                    default: return "✕";
                }
            }
        }
    }
}
