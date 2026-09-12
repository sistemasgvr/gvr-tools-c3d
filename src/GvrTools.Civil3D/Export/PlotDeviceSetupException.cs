using System;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// The plot settings of ONE drawing/layout could not be configured (its saved paper size is not
    /// in the device any more, its page setup is damaged, its plot style table cannot be loaded...).
    ///
    /// Deliberately not an <see cref="ExportSetupException"/>: that type means the run never got off
    /// the ground and nothing can be exported, which aborts a whole batch. A device that worked for
    /// the previous drawing is clearly installed, so failing here must cost that one drawing and let
    /// the rest of the batch continue.
    /// </summary>
    public sealed class PlotDeviceSetupException : Exception
    {
        public PlotDeviceSetupException(string message)
            : base(message)
        {
        }

        public PlotDeviceSetupException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
