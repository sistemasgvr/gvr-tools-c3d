using System.Collections.Generic;
using GvrTools.Civil3D.Model;
using GvrTools.Core.Naming;

namespace GvrTools.Civil3D.Export
{
    /// <summary>
    /// Turns a layout plus a user pattern into a unique file name inside the destination folder.
    /// Equivalent to Revit's <c>ExportFileNamer</c>, retargeted at <see cref="LayoutSnapshot"/>.
    /// </summary>
    public sealed class ExportFileNamer
    {
        private readonly UniqueNameResolver _resolver;
        private readonly IReadOnlyDictionary<string, string> _drawingTokens;
        private readonly string _pattern;
        private readonly string _extension;

        public ExportFileNamer(
            string destinationFolder,
            string pattern,
            string extension,
            IReadOnlyDictionary<string, string> drawingTokens)
        {
            _resolver = new UniqueNameResolver(destinationFolder);
            _pattern = string.IsNullOrWhiteSpace(pattern) ? NamingTokens.DefaultPattern : pattern;
            _extension = extension;
            _drawingTokens = drawingTokens ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// Reserves and returns the base name (no extension) for <paramref name="layout"/>.
        /// </summary>
        public string ReserveBaseName(LayoutSnapshot layout) =>
            _resolver.ReserveBaseName(BuildName(layout), _extension);

        /// <summary>Expands the pattern without reserving anything, for the preview in the UI.</summary>
        public string Preview(LayoutSnapshot layout) => BuildName(layout) + _extension;

        private string BuildName(LayoutSnapshot layout)
        {
            var tokens = new Dictionary<string, string>(_drawingTokens.Count + 4);

            foreach (KeyValuePair<string, string> token in _drawingTokens)
                tokens[token.Key] = token.Value;

            foreach (KeyValuePair<string, string> token in layout.ToTokens())
                tokens[token.Key] = token.Value;

            return FileNameBuilder.Build(_pattern, tokens, layout.Name);
        }
    }
}
