using System.Text;

namespace LiveClaude.Platform.Windows;

/// <summary>
/// Quotes an argument vector into the single command line CreateProcessW takes, the way
/// CommandLineToArgvW parses it back.
///
/// This lives here, not in the shared code, because it is a Windows rule and applying it anywhere
/// else corrupts arguments: POSIX hands the vector to the child verbatim and a quoted
/// <c>"--name"</c> would arrive with its quotes still attached.
/// </summary>
public static class WindowsCommandLine
{
    public static string Build(string executablePath, IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        sb.Append(Quote(executablePath));

        foreach (var argument in arguments)
        {
            sb.Append(' ');
            sb.Append(Quote(argument));
        }

        return sb.ToString();
    }

    public static string Quote(string value)
    {
        if (value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
            return value;

        var sb = new StringBuilder("\"");
        var backslashes = 0;

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\':
                    backslashes++;
                    break;
                case '"':
                    sb.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    break;
                default:
                    sb.Append('\\', backslashes).Append(c);
                    backslashes = 0;
                    break;
            }
        }

        sb.Append('\\', backslashes * 2).Append('"');
        return sb.ToString();
    }
}
