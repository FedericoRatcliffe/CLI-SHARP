using System.Text;
using System.Text.RegularExpressions;

namespace CliSharp.Application.Terminal;

/// <summary>
/// Detects clickable URLs and file paths in terminal text.
/// Ctrl+Click en TerminalCanvas uses this to open links.
/// </summary>
public static partial class LinkDetector
{
    public record DetectedLink(int StartCol, int EndCol, string Uri, LinkType Type);
    public enum LinkType { Url, FilePath }

    // URLs: http:// o https:// seguido de caracteres válidos
    [GeneratedRegex(@"https?://[^\s<>""'\])},;]+", RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    // File paths: ruta con extensión, opcionalmente seguida de :line:col o (line,col)
    // Matchea: src/foo.cs:42:10, C:\path\file.cs(42,10), ./file.py:10
    [GeneratedRegex(@"(?<path>(?:[A-Za-z]:[/\\]|\.{0,2}[/\\])?[\w./\\-]+\.\w{1,10})(?::(?<line>\d+)(?::(?<col>\d+))?|\((?<line>\d+)(?:,(?<col>\d+))?\))?", RegexOptions.Compiled)]
    private static partial Regex FilePathRegex();

    /// <summary>
    /// Detects the link (if any) at position (row, col) del grid.
    /// Returns null if no link under cursor.
    /// </summary>
    public static DetectedLink? DetectAt(Grid grid, int row, int col)
    {
        if (row < 0 || row >= grid.Rows || col < 0 || col >= grid.Columns)
            return null;

        var cells = grid.GetRow(row);
        var sb = new StringBuilder(grid.Columns);
        for (int c = 0; c < grid.Columns; c++)
            sb.Append(cells[c].Character);
        string rowText = sb.ToString();

        // URLs primero (más específico)
        foreach (Match m in UrlRegex().Matches(rowText))
            if (col >= m.Index && col < m.Index + m.Length)
                return new DetectedLink(m.Index, m.Index + m.Length, m.Value, LinkType.Url);

        // File paths
        foreach (Match m in FilePathRegex().Matches(rowText))
        {
            if (col < m.Index || col >= m.Index + m.Length) continue;

            var path = m.Groups["path"].Value;
            var line = m.Groups["line"].Success ? m.Groups["line"].Value : null;
            var colNum = m.Groups["col"].Success ? m.Groups["col"].Value : null;

            // Resolve relative path against CWD
            if (!Path.IsPathRooted(path) && grid.CurrentDirectory is not null)
                path = Path.Combine(grid.CurrentDirectory, path);

            // Build VS Code argument: --goto file:line:col
            var gotoArg = path;
            if (line is not null) gotoArg += $":{line}";
            if (colNum is not null) gotoArg += $":{colNum}";

            return new DetectedLink(m.Index, m.Index + m.Length, gotoArg, LinkType.FilePath);
        }

        return null;
    }
}
