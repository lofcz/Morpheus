namespace Morpheus;

internal static class Extensions
{
    public static string ToFirstUpperRestLower(this string input)
    {
        if (input.Length is 1)
        {
            return input.ToUpperInvariant();
        }
            
        return char.ToUpper(input[0]) + input[1..].ToLowerInvariant();
    }
}