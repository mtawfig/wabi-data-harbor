namespace WabiInsight.Api.Contracts;

/// <summary>
/// Reads a stable code sent as text ("Active", "Suspended") into an enum: names only, case-insensitive, never a number
/// and never an undefined value. An unknown value is a 400 for the caller to fix, not a server error (E3).
/// </summary>
public static class EnumInput
{
    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Any(c => char.IsDigit(c) || c is '-' or ','))
        {
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
