using System;
using System.Collections.Generic;
using System.Globalization;

namespace GvrTools.Core.Text
{
    /// <summary>
    /// Human ordering for identifiers that mix letters and digits, so sheet A-2 sorts before A-10
    /// instead of after it the way a plain string comparison would.
    /// </summary>
    public sealed class NaturalSortComparer : IComparer<string>
    {
        public static readonly NaturalSortComparer Instance = new NaturalSortComparer();

        public int Compare(string left, string right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;

            int i = 0, j = 0;

            while (i < left.Length && j < right.Length)
            {
                if (char.IsDigit(left[i]) && char.IsDigit(right[j]))
                {
                    int startI = i, startJ = j;
                    while (i < left.Length && char.IsDigit(left[i])) i++;
                    while (j < right.Length && char.IsDigit(right[j])) j++;

                    // Compared as numbers, so a leading-zero difference alone is not a difference.
                    string numberLeft = left.Substring(startI, i - startI).TrimStart('0');
                    string numberRight = right.Substring(startJ, j - startJ).TrimStart('0');

                    if (numberLeft.Length != numberRight.Length)
                        return numberLeft.Length - numberRight.Length;

                    int digits = string.Compare(numberLeft, numberRight, StringComparison.Ordinal);
                    if (digits != 0) return digits;

                    continue;
                }

                int chars = char.ToUpper(left[i], CultureInfo.CurrentCulture)
                    .CompareTo(char.ToUpper(right[j], CultureInfo.CurrentCulture));
                if (chars != 0) return chars;

                i++;
                j++;
            }

            return (left.Length - i) - (right.Length - j);
        }
    }
}
