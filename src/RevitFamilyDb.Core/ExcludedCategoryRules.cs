using System;

namespace RevitFamilyDb.Core;

/// <summary>
/// Categorie escluse da sync locale, push cloud e catalogo web.
/// </summary>
public static class ExcludedCategoryRules
{
    private static readonly string[] ExcludedTokens =
    {
        "schedule",
        "sheet",
        "insulation",
        "pattern",
        "masse",
        "mass"
    };

    public static bool IsExcludedCategoryName(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return false;
        }

        var name = categoryName.Trim();
        foreach (var token in ExcludedTokens)
        {
            if (CategoryNameMatchesToken(name, token))
            {
                return true;
            }
        }

        return false;
    }

    private static bool CategoryNameMatchesToken(string categoryName, string token)
    {
        if (categoryName.Equals(token, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (categoryName.StartsWith(token, StringComparison.OrdinalIgnoreCase)
            && categoryName.Length > token.Length
            && !char.IsLetterOrDigit(categoryName[token.Length]))
        {
            return true;
        }

        var spaced = " " + categoryName + " ";
        var needle = " " + token + " ";
        if (spaced.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        if (string.Equals(token, "mass", StringComparison.OrdinalIgnoreCase)
            && categoryName.Equals("masses", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
