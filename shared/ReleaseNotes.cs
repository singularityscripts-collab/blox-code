using System.Text.RegularExpressions;

namespace BloxCode.Core;

/// <summary>
/// The changelog compiled into the app, so "What's new" works without any website: the section of
/// CHANGELOG.md for a version, as plain text for a dialog.
/// </summary>
public static partial class ReleaseNotes
{
    private static readonly Lazy<string> Changelog = new(() =>
    {
        using var stream = typeof(ReleaseNotes).Assembly.GetManifestResourceStream("CHANGELOG.md");
        return stream is null ? "" : new StreamReader(stream).ReadToEnd();
    });

    /// <summary>The title and plain-text body of a version's section, or null when the changelog has none.</summary>
    public static (string Title, string Body)? For(Version version) => Parse(Changelog.Value, version);

    public static (string Title, string Body)? Parse(string changelog, Version version)
    {
        string wanted = $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
        var lines = changelog.Replace("\r\n", "\n").Split('\n');
        int start = Array.FindIndex(lines, l => Heading().Match(l) is { Success: true } m && m.Groups[1].Value == wanted);
        if (start < 0)
            return null;

        string title = Heading().Match(lines[start]).Groups[2].Value.Trim();
        var body = lines.Skip(start + 1).TakeWhile(l => !l.StartsWith("## ")).ToList();
        return (title, ToPlainText(body));
    }

    /// <summary>Markdown to dialog text: headings become plain lines, bullets become "•", wrapped lines join.</summary>
    private static string ToPlainText(List<string> lines)
    {
        var output = new List<string>();
        foreach (string raw in lines)
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (output.Count > 0 && output[^1].Length > 0)
                    output.Add("");
                continue;
            }

            string text = Inline(line.TrimStart());
            if (line.StartsWith("### "))
                output.Add(Inline(line[4..]).ToUpperInvariant());
            else if (line.StartsWith("- "))
                output.Add("• " + Inline(line[2..]));
            else if (output.Count > 0 && output[^1].Length > 0)
                output[^1] += " " + text; // continuation of a wrapped bullet or paragraph
            else
                output.Add(text);
        }

        while (output.Count > 0 && output[^1].Length == 0)
            output.RemoveAt(output.Count - 1);
        return string.Join("\n", output);
    }

    private static string Inline(string text) => Link().Replace(text, "$1").Replace("**", "").Replace("`", "");

    [GeneratedRegex(@"^## (\d+\.\d+\.\d+)\s*(?:—\s*)?(.*)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]+\)")]
    private static partial Regex Link();
}
