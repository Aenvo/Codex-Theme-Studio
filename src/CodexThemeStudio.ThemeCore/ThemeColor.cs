namespace CodexThemeStudio.ThemeCore;

public static class ThemeColor
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (value is null || (value.Length != 7 && value.Length != 9) || value[0] != '#')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            if (!Uri.IsHexDigit(value[index]))
            {
                return false;
            }
        }

        normalized = value.ToUpperInvariant();
        return true;
    }
}

