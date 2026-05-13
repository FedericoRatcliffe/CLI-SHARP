using System.Text;

namespace CliSharp.Application.Terminal;

/// <summary>
/// Tracks commands sent to the PTY and provides fuzzy matching.
/// TrackInput is called with each byte[] sent by the user.
/// </summary>
public sealed class CommandHistory
{
    private readonly List<string> _entries = new();
    private readonly StringBuilder _current = new();
    public int MaxEntries { get; set; } = 500;

    public IReadOnlyList<string> Entries => _entries;

    /// <summary>
    /// Tracks input to detect commands (text + Enter)).
    /// </summary>
    public void TrackInput(byte[] data)
    {
        if (data.Length == 1 && data[0] == 0x0D) // Enter
        {
            var cmd = _current.ToString().Trim();
            if (cmd.Length > 0 && (_entries.Count == 0 || _entries[^1] != cmd))
            {
                _entries.Add(cmd);
                if (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            }
            _current.Clear();
            return;
        }

        if (data.Length == 1 && data[0] == 0x7F) // Backspace
        {
            if (_current.Length > 0) _current.Remove(_current.Length - 1, 1);
            return;
        }

        if (data.Length >= 2 && data[0] == 0x1B) return; // Escape sequence → ignorar
        if (data.Length == 1 && data[0] < 0x20) { _current.Clear(); return; } // Control char → reset

        // Texto imprimible
        var text = Encoding.UTF8.GetString(data);
        if (text.Length > 0 && !char.IsControl(text[0]))
            _current.Append(text);
    }

    /// <summary>
    /// Fuzzy match: retorna score > 0 si todos los chars del query aparecen en orden en candidate.
    /// Bonus for consecutive matches and word starts.
    /// </summary>
    public static int FuzzyScore(string query, string candidate)
    {
        if (string.IsNullOrEmpty(query)) return 1;
        int qi = 0, score = 0;
        bool prevMatch = false;

        for (int ci = 0; ci < candidate.Length && qi < query.Length; ci++)
        {
            if (char.ToLowerInvariant(candidate[ci]) == char.ToLowerInvariant(query[qi]))
            {
                qi++;
                score += prevMatch ? 3 : 1;
                if (ci == 0 || !char.IsLetterOrDigit(candidate[ci - 1])) score += 5;
                prevMatch = true;
            }
            else prevMatch = false;
        }

        return qi == query.Length ? score : 0;
    }
}
