using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;

namespace Koan.Core;

/// <summary>
/// Utilities for de-identifying sensitive values (e.g., connection strings) in logs/health.
/// </summary>
public static class Redaction
{
    private const string Mask = "***";
    private const string NullValue = "(null)";
    private const string MaskedValue = "(masked)";

    private static readonly HashSet<string> SensitiveKeys = new(StringComparer.Ordinal)
    {
        "accesskey",
        "accesskeyid",
        "accesstoken",
        "accountkey",
        "apikey",
        "apitoken",
        "auth",
        "authkey",
        "authtoken",
        "authorization",
        "awsaccesskeyid",
        "awssecretaccesskey",
        "bearertoken",
        "clientsecret",
        "credential",
        "credentials",
        "idtoken",
        "passphrase",
        "password",
        "pwd",
        "refreshtoken",
        "sastoken",
        "sessiontoken",
        "secret",
        "secretaccesskey",
        "secretid",
        "secretkey",
        "sharedaccesskey",
        "sharedaccesssignature",
        "sig",
        "signature",
        "token",
        "uid",
        "user",
        "userid",
        "username",
        "xamzcredential",
        "xamzsecuritytoken",
        "xamzsignature",
        "xgoogcredential",
        "xgoogsignature"
    };

    private static readonly Regex AssignmentRegex = new(
        BuildSensitiveAssignmentPattern(),
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnoreCase |
        RegexOptions.NonBacktracking);

    /// <summary>
    /// Mask sensitive parts of a connection string or URL while preserving useful non-secret structure.
    /// Malformed values that appear to contain credentials fail closed.
    /// </summary>
    public static string DeIdentify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return NullValue;

        try
        {
            if (TryMaskUri(input, out var maskedUri))
            {
                // URI user-info, query, and fragment grammar is handled above. A credential-shaped assignment left
                // elsewhere (for example a non-standard path parameter) is not safely attributable, so fail closed.
                return HasUnmaskedSensitiveAssignment(maskedUri) ? MaskedValue : maskedUri;
            }

            input = MaskEmbeddedUris(input);

            if (!HasSensitiveAssignment(input))
            {
                return input;
            }

            return MaskConnectionString(input);
        }
        catch
        {
            return MaskedValue;
        }
    }

    private static string MaskConnectionString(string input)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = input
        };

        var maskedKnownKey = false;
        foreach (var key in builder.Keys.Cast<string>().ToArray())
        {
            if (!IsSensitiveKey(key)) continue;

            builder[key] = Mask;
            maskedKnownKey = true;
        }

        var result = builder.ConnectionString;

        // A non-standard grammar can make a sensitive assignment part of another value rather than a
        // distinct key. Preserve structure only when the parser proves that every such assignment was masked.
        return maskedKnownKey && !HasUnmaskedSensitiveAssignment(result)
            ? result
            : MaskedValue;
    }

    private static bool TryMaskUri(string input, out string result)
    {
        var schemeDelimiter = input.IndexOf("://", StringComparison.Ordinal);
        if (schemeDelimiter <= 0 || !IsUriScheme(input.AsSpan(0, schemeDelimiter)))
        {
            result = input;
            return false;
        }

        var authorityStart = schemeDelimiter + 3;
        var authorityEnd = IndexOfAny(input, authorityStart, '/', '?', '#');
        if (authorityEnd < 0) authorityEnd = input.Length;

        result = input;
        if (authorityEnd > authorityStart)
        {
            var at = input.LastIndexOf('@', authorityEnd - 1, authorityEnd - authorityStart);
            if (at >= authorityStart)
            {
                result = string.Concat(input.AsSpan(0, authorityStart), Mask, "@", input.AsSpan(at + 1));
            }
        }

        result = MaskUriParameters(result, authorityStart);
        return true;
    }

    private static string MaskEmbeddedUris(string input)
    {
        var output = new StringBuilder(input.Length);
        var copiedThrough = 0;
        var searchFrom = 0;
        var found = false;

        while (searchFrom < input.Length)
        {
            var delimiter = input.IndexOf("://", searchFrom, StringComparison.Ordinal);
            if (delimiter < 0) break;

            var schemeStart = delimiter - 1;
            while (schemeStart >= 0 && IsUriSchemeCharacter(input[schemeStart])) schemeStart--;
            schemeStart++;
            if (schemeStart >= delimiter || !IsUriScheme(input.AsSpan(schemeStart, delimiter - schemeStart)) ||
                schemeStart > 0 && IsUriSchemeCharacter(input[schemeStart - 1]))
            {
                searchFrom = delimiter + 3;
                continue;
            }

            var uriEnd = delimiter + 3;
            while (uriEnd < input.Length && !IsEmbeddedUriTerminator(input[uriEnd])) uriEnd++;

            var token = input[schemeStart..uriEnd];
            if (!TryMaskUri(token, out var masked))
            {
                searchFrom = uriEnd;
                continue;
            }

            output.Append(input, copiedThrough, schemeStart - copiedThrough);
            output.Append(masked);
            copiedThrough = uriEnd;
            searchFrom = uriEnd;
            found = true;
        }

        if (!found) return input;
        output.Append(input, copiedThrough, input.Length - copiedThrough);
        return output.ToString();
    }

    private static string MaskUriParameters(string input, int searchStart)
    {
        var queryStart = input.IndexOf('?', searchStart);
        var result = input;
        if (queryStart >= 0)
        {
            var fragmentStart = input.IndexOf('#', queryStart + 1);
            var queryEnd = fragmentStart >= 0 ? fragmentStart : input.Length;
            var query = input.AsSpan(queryStart + 1, queryEnd - queryStart - 1);
            var maskedQuery = MaskQueryParameters(query);

            result = string.Concat(
                input.AsSpan(0, queryStart + 1),
                maskedQuery,
                input.AsSpan(queryEnd));
        }

        var fragmentDelimiter = result.IndexOf('#', searchStart);
        if (fragmentDelimiter < 0) return result;

        var fragment = result.AsSpan(fragmentDelimiter + 1);
        var maskedFragment = MaskQueryParameters(fragment);
        return string.Concat(result.AsSpan(0, fragmentDelimiter + 1), maskedFragment);
    }

    private static string MaskQueryParameters(ReadOnlySpan<char> query)
    {
        var builder = new StringBuilder(query.Length);
        var position = 0;

        while (position < query.Length)
        {
            var segmentEnd = FindParameterDelimiter(query, position, includeSemicolon: true);
            if (segmentEnd < 0) segmentEnd = query.Length;

            var equalsOffset = query[position..segmentEnd].IndexOf('=');
            if (equalsOffset < 0 ||
                !IsSensitiveKey(DecodeQueryKey(query[position..(position + equalsOffset)])))
            {
                builder.Append(query[position..segmentEnd]);
                if (segmentEnd < query.Length) builder.Append(query[segmentEnd]);
                position = segmentEnd + 1;
                continue;
            }

            var equals = position + equalsOffset;
            builder.Append(query[position..(equals + 1)]);
            builder.Append(Mask);

            var valueEnd = FindSensitiveParameterEnd(query, equals + 1);
            if (valueEnd >= query.Length) break;

            builder.Append(query[valueEnd]);
            position = valueEnd + 1;
        }

        return builder.ToString();
    }

    private static int FindSensitiveParameterEnd(ReadOnlySpan<char> query, int valueStart)
    {
        while (valueStart < query.Length && char.IsWhiteSpace(query[valueStart])) valueStart++;

        if (valueStart >= query.Length || query[valueStart] is not ('"' or '\''))
        {
            var delimiter = FindParameterDelimiter(query, valueStart, includeSemicolon: false);
            return delimiter >= 0 ? delimiter : query.Length;
        }

        var quote = query[valueStart];
        for (var index = valueStart + 1; index < query.Length; index++)
        {
            if (query[index] != quote) continue;
            if (query[index - 1] == '\\') continue;
            if (index + 1 < query.Length && query[index + 1] == quote)
            {
                index++;
                continue;
            }

            var delimiter = FindParameterDelimiter(query, index + 1, includeSemicolon: true);
            return delimiter >= 0 ? delimiter : query.Length;
        }

        return query.Length;
    }

    private static int FindParameterDelimiter(ReadOnlySpan<char> query, int startIndex, bool includeSemicolon)
    {
        for (var index = startIndex; index < query.Length; index++)
        {
            if (query[index] == '&' || includeSemicolon && query[index] == ';') return index;
        }

        return -1;
    }

    private static string DecodeQueryKey(ReadOnlySpan<char> key)
    {
        var encoded = key.ToString().Replace("+", " ", StringComparison.Ordinal);
        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch
        {
            return encoded;
        }
    }

    private static bool HasSensitiveAssignment(string input) => AssignmentRegex.IsMatch(input);

    private static bool HasUnmaskedSensitiveAssignment(string input)
    {
        foreach (Match match in AssignmentRegex.Matches(input))
        {
            var valueStart = match.Index + match.Length;
            while (valueStart < input.Length && char.IsWhiteSpace(input[valueStart])) valueStart++;
            var quote = valueStart < input.Length && input[valueStart] is '"' or '\''
                ? input[valueStart++]
                : (char?)null;

            if (!input.AsSpan(valueStart).StartsWith(Mask, StringComparison.Ordinal) ||
                !IsMaskedValueBoundary(input, valueStart + Mask.Length, quote))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMaskedValueBoundary(string input, int index, char? quote)
    {
        if (index >= input.Length) return quote is null;
        if (quote is not null) return input[index] == quote;

        return char.IsWhiteSpace(input[index]) || input[index] is ';' or ',' or '&' or '?' or '#';
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new StringBuilder(key.Length);

        foreach (var character in key)
        {
            if (!char.IsLetterOrDigit(character)) continue;
            normalized.Append(char.ToLowerInvariant(character));
        }

        return SensitiveKeys.Contains(normalized.ToString());
    }

    private static bool IsUriScheme(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || !char.IsAsciiLetter(value[0])) return false;

        foreach (var character in value[1..])
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsUriSchemeCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '+' or '-' or '.';

    private static bool IsEmbeddedUriTerminator(char character)
        => char.IsWhiteSpace(character) || character is ';' or ',' or '"' or '\'' or ')' or ']' or '}' or '<' or '>';

    private static string BuildSensitiveAssignmentPattern()
    {
        var alternatives = SensitiveKeys
            .OrderByDescending(key => key.Length)
            .ThenBy(key => key, StringComparer.Ordinal)
            .Select(key => string.Join(@"[\s_.\-]*", key.Select(character => Regex.Escape(character.ToString()))));

        return $"(?:^|[^A-Za-z0-9])['\"\\[(]?(?<key>{string.Join('|', alternatives)})[\\s_.\\-'\"\\])]*=";
    }

    private static int IndexOfAny(string input, int startIndex, params char[] values)
    {
        for (var index = startIndex; index < input.Length; index++)
        {
            if (values.Contains(input[index])) return index;
        }

        return -1;
    }
}
