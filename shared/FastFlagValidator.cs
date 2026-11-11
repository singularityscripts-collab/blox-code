using System.Text.Json;
using System.Text.RegularExpressions;

namespace BloxCode.Core;

public enum FlagKind { Bool, Int, String, Log, Unknown }

/// <summary>
/// Flag-name and value rules recovered from Bloxstrap's FastFlagEditorPage: the accepted prefixes,
/// the _PlaceFilter / _DataCenterFilter suffixes and their value patterns.
/// </summary>
public static partial class FastFlagValidator
{
    private static readonly (string Prefix, FlagKind Kind)[] Prefixes =
    {
        // Longest first so "DFFlag" isn't read as "D" + "FFlag".
        ("DFString", FlagKind.String), ("FString", FlagKind.String),
        ("DFFlag", FlagKind.Bool), ("SFFlag", FlagKind.Bool), ("FFlag", FlagKind.Bool),
        ("DFInt", FlagKind.Int), ("FInt", FlagKind.Int),
        ("DFLog", FlagKind.Log), ("FLog", FlagKind.Log),
    };

    private static readonly string[] FilterSuffixes = { "_PlaceFilter", "_DataCenterFilter" };

    [GeneratedRegex(@"^(?:true|false)(;[\d]{1,})+$", RegexOptions.IgnoreCase)]
    private static partial Regex BoolFilter();

    [GeneratedRegex(@"^([\d]{1,})?(;[\d]{1,})+$")]
    private static partial Regex IntFilter();

    [GeneratedRegex(@"^[^;]*(;[\d]{1,})+$")]
    private static partial Regex StringFilter();

    [GeneratedRegex(@"^[A-Za-z0-9_]+$")]
    private static partial Regex NameCharacters();

    public static FlagKind KindOf(string name)
    {
        foreach (var (prefix, kind) in Prefixes)
            if (name.StartsWith(prefix, StringComparison.Ordinal))
                return kind;
        return FlagKind.Unknown;
    }

    public static string Describe(FlagKind kind) => kind switch
    {
        FlagKind.Bool => "Boolean",
        FlagKind.Int => "Number",
        FlagKind.String => "Text",
        FlagKind.Log => "Log level",
        _ => "Unknown",
    };

    private static bool IsFiltered(string name) => FilterSuffixes.Any(s => name.EndsWith(s, StringComparison.Ordinal));

    /// <summary>Returns a human-readable problem with the name, or null when it is valid.</summary>
    public static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Give the flag a name.";
        if (!NameCharacters().IsMatch(name))
            return "Flag names only use letters, numbers and underscores.";
        if (KindOf(name) == FlagKind.Unknown)
            return "Names start with FFlag, DFFlag, SFFlag, FInt, DFInt, FString, DFString, FLog or DFLog.";
        return null;
    }

    /// <summary>Returns a human-readable problem with the value for this flag, or null when it is valid.</summary>
    public static string? ValidateValue(string name, string value)
    {
        var kind = KindOf(name);

        if (IsFiltered(name))
        {
            bool ok = kind switch
            {
                FlagKind.Bool => BoolFilter().IsMatch(value),
                FlagKind.Int or FlagKind.Log => IntFilter().IsMatch(value),
                _ => StringFilter().IsMatch(value),
            };
            return ok ? null : "Filter flags take value;id;id… (for example True;1818).";
        }

        return kind switch
        {
            FlagKind.Bool when !value.Equals("true", StringComparison.OrdinalIgnoreCase) && !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                => "Boolean flags are True or False.",
            FlagKind.Int or FlagKind.Log when !long.TryParse(value, out _)
                => "This flag needs a whole number.",
            _ => null,
        };
    }

    /// <summary>Booleans are stored as "True"/"False" like Bloxstrap; everything else as typed.</summary>
    public static string Normalize(string name, string value)
    {
        value = value.Trim();
        if (KindOf(name) == FlagKind.Bool && !IsFiltered(name))
        {
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return "True";
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return "False";
        }
        return value;
    }

    /// <summary>
    /// The flags with a valid name and value (normalized), and how many were left out: what an
    /// imported file or profile may bring in.
    /// </summary>
    public static (Dictionary<string, string> Valid, int Skipped) ValidOnly(IReadOnlyDictionary<string, string> flags)
    {
        var valid = new Dictionary<string, string>();
        foreach (var (name, value) in flags)
        {
            if (value is not null && ValidateName(name) is null && ValidateValue(name, value) is null)
                valid[name] = Normalize(name, value);
        }
        return (valid, flags.Count - valid.Count);
    }

    /// <summary>
    /// Parses pasted JSON. Like Bloxstrap it tolerates a missing pair of braces, so a few
    /// "Name": value lines copied out of a bigger file still import.
    /// </summary>
    public static Dictionary<string, string> ParseJson(string text)
    {
        text = text.Trim().TrimEnd(',');
        if (!text.StartsWith('{'))
            text = "{" + text + "}";

        using var doc = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected an object of flag names and values.");

        var flags = new Dictionary<string, string>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            string value = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString()!,
                JsonValueKind.True => "True",
                JsonValueKind.False => "False",
                _ => prop.Value.GetRawText(),
            };
            flags[prop.Name.Trim()] = Normalize(prop.Name.Trim(), value);
        }
        return flags;
    }
}
