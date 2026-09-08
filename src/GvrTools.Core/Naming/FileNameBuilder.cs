using System;
using System.Collections.Generic;
using System.Text;

namespace GvrTools.Core.Naming
{
    /// <summary>
    /// Expands a user-editable pattern such as <c>{SheetNumber} - {SheetName}</c> against a bag of
    /// token values.
    ///
    /// The expander knows nothing about sheets or Revit on purpose: the caller decides which tokens
    /// exist (see <c>GvrTools.Revit.Export.NamingTokens</c>), which is what lets a future tool reuse
    /// the same pattern box with a completely different token set.
    /// </summary>
    public static class FileNameBuilder
    {
        private const char TokenOpen = '{';
        private const char TokenClose = '}';
        private const string Separator = " - ";

        /// <summary>
        /// Expands <paramref name="pattern"/> and sanitises the result. Unknown or empty tokens
        /// collapse together with the separator around them, so
        /// <c>{SheetNumber} - {RevisionNumber}</c> yields <c>A-101</c> rather than <c>A-101 -</c>
        /// on a sheet that has no revision yet.
        /// </summary>
        public static string Build(string pattern, IReadOnlyDictionary<string, string> tokens, string fallback)
        {
            string expanded = TrimDanglingSeparators(Expand(pattern, tokens));

            return PathSanitizer.SanitizeFileName(expanded, PathSanitizer.SanitizeFileName(fallback));
        }

        /// <summary>Raw token substitution without sanitising. Exposed so the UI can show a preview.</summary>
        public static string Expand(string pattern, IReadOnlyDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(pattern)) return string.Empty;

            var sb = new StringBuilder(pattern.Length + 16);
            int index = 0;

            while (index < pattern.Length)
            {
                int open = pattern.IndexOf(TokenOpen, index);
                int close = open < 0 ? -1 : pattern.IndexOf(TokenClose, open + 1);

                if (close < 0)
                {
                    sb.Append(pattern, index, pattern.Length - index);
                    break;
                }

                sb.Append(pattern, index, open - index);

                string name = pattern.Substring(open + 1, close - open - 1);
                if (tokens != null && tokens.TryGetValue(name, out string value))
                    sb.Append(value ?? string.Empty);

                index = close + 1;
            }

            return sb.ToString();
        }

        /// <summary>Drops separator runs left behind by tokens that expanded to nothing.</summary>
        private static string TrimDanglingSeparators(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            string[] parts = value.Split(new[] { Separator }, StringSplitOptions.None);
            var kept = new List<string>(parts.Length);

            foreach (string part in parts)
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0) kept.Add(trimmed);
            }

            return string.Join(Separator, kept.ToArray()).Trim();
        }
    }
}
