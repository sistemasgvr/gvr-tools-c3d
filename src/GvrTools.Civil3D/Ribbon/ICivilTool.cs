using System;
using System.Windows.Media;

namespace GvrTools.Civil3D.Ribbon
{
    /// <summary>
    /// One button on the GVR Tools ribbon.
    ///
    /// This is the whole extension contract of the suite: a new tool implements this interface in
    /// its own assembly, and the host application discovers it, places it on the right panel and
    /// wires the button to its command. No file in GvrTools.App has to change to add a tool.
    ///
    /// Unlike Revit's <c>IExternalCommand</c>, AutoCAD commands are invoked by name through
    /// <c>[CommandMethod]</c> rather than by type, so each tool also declares the global command
    /// name Revit... no, AutoCAD, resolves when the button is clicked.
    /// </summary>
    public interface ICivilTool
    {
        /// <summary>
        /// Stable, unique identifier. Used as the internal ribbon button name, so it must not
        /// change once shipped.
        /// </summary>
        string Id { get; }

        /// <summary>Ribbon label. A line break splits it across two lines on the button.</summary>
        string Title { get; }

        /// <summary>Panel the button belongs to; panels are created on demand and shared.</summary>
        string PanelName { get; }

        /// <summary>Order within the panel, lowest first. Ties fall back to the title.</summary>
        int SortOrder { get; }

        string Tooltip { get; }

        /// <summary>Extended help shown in the expanded tooltip. Optional.</summary>
        string LongDescription { get; }

        /// <summary>
        /// Global AutoCAD command name the button runs (the string that appears after
        /// <c>[CommandMethod("...")]</c> in the tool's own assembly).
        /// </summary>
        string CommandName { get; }

        /// <summary>Button icon, or null to fall back to the suite's generic icon.</summary>
        ImageSource CreateIcon();

        /// <summary>
        /// Lets a tool opt out on hosts it cannot support. Returning false simply omits the button.
        /// </summary>
        bool IsSupported();

        /// <summary>
        /// Feature code del License API requerido para mostrar el botón (ej. tool.batch_export).
        /// Null o vacío = siempre visible.
        /// </summary>
        string RequiredFeature { get; }
    }
}
