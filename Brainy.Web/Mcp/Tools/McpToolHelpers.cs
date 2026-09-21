namespace Brainy.Web.Mcp.Tools;

/// <summary>
/// Shared helpers for MCP tools. Enum-valued inputs are accepted as strings (case-insensitive)
/// with an explicit, allowed-values error, rather than raw integers — clearer for an LLM caller
/// and unambiguous in the tool schema.
/// </summary>
internal static class McpToolHelpers
{
    /// <summary>
    /// Parses an optional enum value. Returns <c>null</c> for a blank input (meaning "leave
    /// unchanged" on an update), and throws <see cref="ArgumentException"/> with the allowed
    /// values for anything non-blank that does not match.
    /// </summary>
    public static TEnum? ParseEnum<TEnum>(string? value, string paramName)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            return parsed;

        throw new ArgumentException(
            $"'{value}' is not a valid {typeof(TEnum).Name}. Allowed values: {string.Join(", ", Enum.GetNames<TEnum>())}.",
            paramName);
    }
}
