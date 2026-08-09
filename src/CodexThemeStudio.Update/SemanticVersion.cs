using System.Globalization;

namespace CodexThemeStudio.Update;

public readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease = null) : IComparable<SemanticVersion>
{
    public static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('v'))
        {
            text = text[1..];
        }

        text = text.Split('+', 2)[0];
        var split = text.Split('-', 2);
        var numbers = split[0].Split('.');
        if (numbers.Length != 3 ||
            !TryNumber(numbers[0], out var major) ||
            !TryNumber(numbers[1], out var minor) ||
            !TryNumber(numbers[2], out var patch))
        {
            return false;
        }

        var preRelease = split.Length == 2 ? split[1] : null;
        if (preRelease is { Length: 0 } ||
            preRelease?.Split('.').Any(static item =>
                item.Length == 0 ||
                item.Any(static c => !char.IsAsciiLetterOrDigit(c) && c != '-')) == true)
        {
            return false;
        }

        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var result = Major.CompareTo(other.Major);
        if (result == 0) result = Minor.CompareTo(other.Minor);
        if (result == 0) result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;

        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            var leftNumeric = int.TryParse(left[index], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = int.TryParse(right[index], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric != rightNumeric)
            {
                result = leftNumeric ? -1 : 1;
            }
            else
            {
                result = string.Compare(left[index], right[index], StringComparison.Ordinal);
            }

            if (result != 0) return result;
        }

        return 0;
    }

    public override string ToString() =>
        $"{Major}.{Minor}.{Patch}{(PreRelease is null ? string.Empty : $"-{PreRelease}")}";

    private static bool TryNumber(string text, out int value)
    {
        value = 0;
        return text.Length > 0 &&
            (text.Length == 1 || text[0] != '0') &&
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
