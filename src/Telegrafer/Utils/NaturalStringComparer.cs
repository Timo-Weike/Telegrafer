using System.Globalization;

namespace Telegrafer.Utils
{
    /// <summary>
    /// Compares strings case-insensitively and numbers by their value, so that "Steuerung 2" is sorted before
    /// "Steuerung 10".
    /// </summary>
    public sealed class NaturalStringComparer : IComparer<string?>
    {
        public static readonly NaturalStringComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x is null)
            {
                return -1;
            }

            if (y is null)
            {
                return 1;
            }

            var compareInfo = CultureInfo.CurrentCulture.CompareInfo;
            int i = 0, j = 0;

            while (i < x.Length && j < y.Length)
            {
                var startX = i;
                var startY = j;

                if (IsDigit(x[i]) && IsDigit(y[j]))
                {
                    while (i < x.Length && IsDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && IsDigit(y[j]))
                    {
                        j++;
                    }

                    var numberX = x.AsSpan(startX, i - startX).TrimStart('0');
                    var numberY = y.AsSpan(startY, j - startY).TrimStart('0');

                    if (numberX.Length != numberY.Length)
                    {
                        return numberX.Length.CompareTo(numberY.Length);
                    }

                    var result = numberX.CompareTo(numberY, StringComparison.Ordinal);

                    if (result != 0)
                    {
                        return result;
                    }
                }
                else
                {
                    while (i < x.Length && !IsDigit(x[i]))
                    {
                        i++;
                    }

                    while (j < y.Length && !IsDigit(y[j]))
                    {
                        j++;
                    }

                    var result = compareInfo.Compare(
                        x.AsSpan(startX, i - startX),
                        y.AsSpan(startY, j - startY),
                        CompareOptions.IgnoreCase);

                    if (result != 0)
                    {
                        return result;
                    }
                }
            }

            return (x.Length - i).CompareTo(y.Length - j);
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
