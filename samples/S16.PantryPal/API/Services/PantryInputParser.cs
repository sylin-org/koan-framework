using System.Globalization;
using System.Text.RegularExpressions;
using S16.PantryPal.Models;

namespace S16.PantryPal.Services;

/// <summary>
/// Parses natural language input for pantry items.
/// Handles quantity and expiration date extraction from flexible user input.
/// </summary>
public interface IPantryInputParser
{
    /// <summary>
    /// Parse natural language input like "5 lbs, expires in a week"
    /// </summary>
    ParsedItemData ParseInput(string input, ParserContext? context = null);
}

/// <summary>
/// Context for parser customization
/// </summary>
public class ParserContext
{
    public DateTime ReferenceDate { get; set; } = DateTime.UtcNow;
    public string? ItemCategory { get; set; }
    public string[]? KnownUnits { get; set; }
}

/// <summary>
/// Premium natural language parser for pantry item input.
/// Supports flexible quantity and expiration date formats.
/// </summary>
public class PantryInputParser : IPantryInputParser
{
    // Unit normalization lookup
    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Weight
        ["lb"] = "lbs", ["pound"] = "lbs", ["pounds"] = "lbs",
        ["oz"] = "oz", ["ounce"] = "oz", ["ounces"] = "oz",
        ["g"] = "grams", ["gram"] = "grams",
        ["kg"] = "kilograms", ["kilogram"] = "kilograms",

        // Volume
        ["cup"] = "cup", ["cups"] = "cup",
        ["tbsp"] = "tbsp", ["tablespoon"] = "tbsp", ["tablespoons"] = "tbsp",
        ["tsp"] = "tsp", ["teaspoon"] = "tsp", ["teaspoons"] = "tsp",
        ["ml"] = "ml", ["milliliter"] = "ml", ["milliliters"] = "ml",
        ["l"] = "liter", ["liter"] = "liter", ["liters"] = "liter",

        // Count
        ["can"] = "whole", ["cans"] = "whole",
        ["jar"] = "whole", ["jars"] = "whole",
        ["bottle"] = "whole", ["bottles"] = "whole",
        ["pack"] = "whole", ["packs"] = "whole",
        ["package"] = "whole", ["packages"] = "whole",
        ["unit"] = "whole", ["units"] = "whole",
        ["piece"] = "whole", ["pieces"] = "whole",
        ["item"] = "whole", ["items"] = "whole"
    };

    public ParsedItemData ParseInput(string input, ParserContext? context = null)
    {
        context ??= new ParserContext();
        var result = new ParsedItemData { WasParsed = false };
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(input))
            return result;

        // Split by common delimiters
        var parts = SplitInput(input);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // Try quantity parsing first
            if (TryParseQuantity(trimmed, out var qty, out var unit))
            {
                result.Quantity = qty;
                result.Unit = NormalizeUnit(unit);
                result.QuantityRawInput = part;
            }
            // Try expiration parsing
            else if (IsExpirationPhrase(trimmed))
            {
                var expiration = ParseExpiration(trimmed, context.ReferenceDate);
                result.ExpiresAt = expiration.Date;
                result.ExpirationRawInput = part;
                result.Confidence = expiration.Confidence;

                if (expiration.Warning != null)
                    warnings.Add(expiration.Warning);
            }
        }

        result.WasParsed = result.Quantity.HasValue || result.ExpiresAt.HasValue;
        result.ParseWarnings = warnings.Count > 0 ? warnings.ToArray() : null;

        return result;
    }

    private string[] SplitInput(string input)
    {
        // Split on: comma, semicolon, "and", newline
        return Regex.Split(input, @"[,;]|\band\b|\r?\n", RegexOptions.IgnoreCase)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }

    private bool TryParseQuantity(string input, out decimal quantity, out string unit)
    {
        quantity = 0;
        unit = "";

        // Patterns:
        // "5 lbs", "2.5 oz", "3 cans", "1 jar", "2" (assume whole)
        // "1/2 cup", "3 1/2 lbs" (fractions)

        // Try decimal with unit
        var match = Regex.Match(input, @"^(\d+(?:\.\d+)?)\s*([a-z]+)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            quantity = decimal.Parse(match.Groups[1].Value);
            unit = match.Groups[2].Success ? match.Groups[2].Value : "whole";
            return true;
        }

        // Try fraction: "1/2 cup"
        match = Regex.Match(input, @"^(\d+)/(\d+)\s*([a-z]+)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var numerator = decimal.Parse(match.Groups[1].Value);
            var denominator = decimal.Parse(match.Groups[2].Value);
            quantity = numerator / denominator;
            unit = match.Groups[3].Success ? match.Groups[3].Value : "whole";
            return true;
        }

        // Try mixed fraction: "3 1/2 lbs"
        match = Regex.Match(input, @"^(\d+)\s+(\d+)/(\d+)\s*([a-z]+)?$", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var whole = decimal.Parse(match.Groups[1].Value);
            var numerator = decimal.Parse(match.Groups[2].Value);
            var denominator = decimal.Parse(match.Groups[3].Value);
            quantity = whole + (numerator / denominator);
            unit = match.Groups[4].Success ? match.Groups[4].Value : "whole";
            return true;
        }

        return false;
    }

    private string NormalizeUnit(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
            return "whole";

        var lower = unit.Trim().ToLowerInvariant();
        return UnitAliases.TryGetValue(lower, out var normalized) ? normalized : lower;
    }

    private bool IsExpirationPhrase(string input)
    {
        var lower = input.ToLowerInvariant();

        return lower.Contains("expire") ||
               lower.Contains("best by") ||
               lower.Contains("use by") ||
               lower.Contains("good until") ||
               lower.Contains("best before") ||
               Regex.IsMatch(input, @"\d{4}-\d{2}-\d{2}") || // ISO date
               Regex.IsMatch(input, @"\d{1,2}/\d{1,2}"); // MM/DD or M/D
    }

    private (DateTime? Date, ExpirationParseConfidence Confidence, string? Warning) ParseExpiration(
        string input,
        DateTime referenceDate)
    {
        // Remove common prefixes
        var cleaned = Regex.Replace(input, @"^(expires?|best by|use by|good until|best before)\s*", "", RegexOptions.IgnoreCase).Trim();

        // Pattern 1: ISO date "2025-10-10"
        if (DateTime.TryParseExact(cleaned, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
            return (isoDate, ExpirationParseConfidence.High, null);

        // Pattern 2: Common date formats
        if (DateTime.TryParse(cleaned, out var parsedDate))
        {
            // If parsed date is in the past, assume next year
            if (parsedDate < referenceDate)
            {
                parsedDate = parsedDate.AddYears(1);
                return (parsedDate, ExpirationParseConfidence.Medium, "Date was in past, assuming next year");
            }
            return (parsedDate, ExpirationParseConfidence.High, null);
        }

        // Pattern 3: Relative time "in X days/weeks/months"
        var relativeMatch = Regex.Match(cleaned, @"^in\s+(\d+)\s+(day|week|month|year)s?$", RegexOptions.IgnoreCase);
        if (relativeMatch.Success)
        {
            var amount = int.Parse(relativeMatch.Groups[1].Value);
            var unit = relativeMatch.Groups[2].Value.ToLowerInvariant();

            var result = unit switch
            {
                "day" => referenceDate.AddDays(amount),
                "week" => referenceDate.AddDays(amount * 7),
                "month" => referenceDate.AddMonths(amount),
                "year" => referenceDate.AddYears(amount),
                _ => referenceDate
            };

            return (result, ExpirationParseConfidence.High, null);
        }

        // Pattern 4: "next week", "next month"
        if (cleaned.StartsWith("next ", StringComparison.OrdinalIgnoreCase))
        {
            var unit = cleaned.Substring(5).Trim().ToLowerInvariant();

            var result = unit switch
            {
                "week" => referenceDate.AddDays(7),
                "month" => referenceDate.AddMonths(1),
                "year" => referenceDate.AddYears(1),
                _ => (DateTime?)null
            };

            if (result.HasValue)
                return (result, ExpirationParseConfidence.Medium, null);
        }

        // Pattern 5: "tomorrow", "today"
        var lowerCleaned = cleaned.ToLowerInvariant();
        if (lowerCleaned == "tomorrow")
            return (referenceDate.AddDays(1), ExpirationParseConfidence.High, null);

        if (lowerCleaned == "today")
            return (referenceDate, ExpirationParseConfidence.High, null);

        // Pattern 6: "this week", "this month"
        if (lowerCleaned.StartsWith("this "))
        {
            var unit = lowerCleaned.Substring(5).Trim();

            var result = unit switch
            {
                "week" => referenceDate.AddDays(7 - (int)referenceDate.DayOfWeek), // End of this week
                "month" => new DateTime(referenceDate.Year, referenceDate.Month, DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month)),
                _ => (DateTime?)null
            };

            if (result.HasValue)
                return (result, ExpirationParseConfidence.Medium, "Interpreted as end of period");
        }

        // Pattern 7: Month names "March 15", "Oct 10", "December"
        var monthMatch = Regex.Match(cleaned, @"(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)\w*\s*(\d{1,2})?", RegexOptions.IgnoreCase);
        if (monthMatch.Success)
        {
            var monthStr = monthMatch.Groups[1].Value;
            var hasDay = monthMatch.Groups[2].Success;
            var day = hasDay ? int.Parse(monthMatch.Groups[2].Value) : 1;

            var month = DateTime.ParseExact(monthStr, "MMM", CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces).Month;

            // Assume this year unless month has passed
            var year = referenceDate.Year;
            if (month < referenceDate.Month || (month == referenceDate.Month && day < referenceDate.Day))
                year++;

            try
            {
                var result = new DateTime(year, month, day);
                var warning = year > referenceDate.Year ? "Assuming next year" : null;
                return (result, ExpirationParseConfidence.Medium, warning);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Invalid day for month
                return (null, ExpirationParseConfidence.Unparsed, "Invalid date");
            }
        }

        // Pattern 8: Simple numbers "10" or "15" (assume days from now)
        if (int.TryParse(cleaned, out var days) && days > 0 && days <= 365)
        {
            return (referenceDate.AddDays(days), ExpirationParseConfidence.Low, "Interpreted as days from now");
        }

        // Couldn't parse
        return (null, ExpirationParseConfidence.Unparsed, "Could not parse expiration date");
    }
}
