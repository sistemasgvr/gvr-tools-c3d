using System.Collections.Generic;
using System.Linq;

namespace GvrTools.Civil3D.Export
{
    /// <summary>One placeholder the user can put in the file-name pattern.</summary>
    public sealed class NamingToken
    {
        public NamingToken(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }

        public string Description { get; }

        /// <summary>The token as typed in a pattern, e.g. <c>{LayoutName}</c>.</summary>
        public string Placeholder => "{" + Name + "}";
    }

    /// <summary>
    /// The tokens the layout exporter understands, in the order they are offered in the UI.
    ///
    /// Single source of truth: <see cref="ExportFileNamer"/> resolves exactly these, and the window
    /// builds its help text from the same list.
    /// </summary>
    public static class NamingTokens
    {
        public const string DefaultPattern = "{LayoutName}";

        public static readonly IReadOnlyList<NamingToken> All = new[]
        {
            new NamingToken("LayoutName", "Nombre de la presentación (layout)"),
            new NamingToken("PageSetupName", "Nombre de la configuración de página asignada"),
            new NamingToken("DrawingTitle", "Nombre del archivo DWG"),
            new NamingToken("Date", "Fecha de hoy (AAAA-MM-DD)")
        };

        /// <summary>One-line hint listing every token, for the tooltip under the pattern box.</summary>
        public static string HelpText =>
            string.Join("  ", All.Select(token => token.Placeholder).ToArray());
    }
}
