using System.Text;

namespace LiveClaude.Abstractions;

/// <summary>Shared helpers for building and parsing CLI argument lists.</summary>
public static class CliArguments
{
    /// <summary>Splits a raw extra-arguments string, honouring double quotes.</summary>
    public static IReadOnlyList<string> Split(string input)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"')
                inQuotes = !inQuotes;
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
            }
            else
                current.Append(c);
        }

        if (current.Length > 0)
            result.Add(current.ToString());

        return result;
    }
}
