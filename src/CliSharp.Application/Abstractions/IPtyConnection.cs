namespace CliSharp.Application.Abstractions;

/// <summary>
/// Abstraction over a pseudo-terminal.
/// Infrastructure implements it with ConPTY (Windows)).
/// </summary>
public interface IPtyConnection : IAsyncDisposable
{
    /// <summary>
    /// Stream to send input to the process (keystrokes).
    /// </summary>
    Stream InputStream { get; }

    /// <summary>
    /// Stream to read output from the process (secuencias VT).
    /// </summary>
    Stream OutputStream { get; }

    /// <summary>
    /// Fires when the child process terminates.
    /// </summary>
    event Action? ProcessExited;

    /// <summary>
    /// Resizes the pseudo-terminal.
    /// </summary>
    void Resize(int columns, int rows);
}
