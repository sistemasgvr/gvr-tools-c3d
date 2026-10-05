using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using GvrTools.Core.IO;

namespace GvrTools.Civil3D.Export.Plotting
{
    /// <summary>The drawing's scale list (SCALELISTEDIT), which is what the Plot dialog's scale combo shows.</summary>
    public static class PlotScaleRepository
    {
        /// <summary>
        /// The drawing's scales in list order, or <see cref="PlotScaleList.Default"/> when the
        /// drawing has none (or there is no drawing, as in the folder exporter).
        /// </summary>
        public static IReadOnlyList<PlotScale> GetScales(Database database)
        {
            var scales = new List<PlotScale>();

            try
            {
                ObjectContextCollection collection =
                    database?.ObjectContextManager?.GetContextCollection("ACDB_ANNOTATIONSCALES");

                if (collection != null)
                {
                    foreach (ObjectContext context in collection)
                    {
                        if (context is AnnotationScale scale && !scale.IsTemporaryScale)
                            scales.Add(new PlotScale(scale.Name, scale.PaperUnits, scale.DrawingUnits));
                    }
                }
            }
            catch (Exception)
            {
                // Sin lista de escalas legible se usa la lista métrica por defecto.
            }

            return PlotScaleList.Normalize(scales);
        }
    }
}
