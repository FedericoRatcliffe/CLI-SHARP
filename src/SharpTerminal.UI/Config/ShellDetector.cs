using System.Diagnostics;

namespace SharpTerminal.UI.Config;

/// <summary>
/// Discovers shells installed on the machine and merges them with the user's
/// config-defined entries. Config entries are listed first; auto-detected
/// entries are appended only if no config entry matches by command basename.
/// </summary>
public static class ShellDetector
{
    public static List<ShellEntry> Resolve(AppConfig config)
    {
        var result = new List<ShellEntry>(config.Shells);
        foreach (var auto in AutoDetected())
        {
            bool duplicate = result.Any(s =>
                string.Equals(Basename(s.Command), Basename(auto.Command),
                    StringComparison.OrdinalIgnoreCase));
            if (!duplicate) result.Add(auto);
        }
        return result;
    }

    private static IEnumerable<ShellEntry> AutoDetected()
    {
        if (Exists("pwsh.exe", "--version"))
            yield return new ShellEntry { Name = "PowerShell 7+", Command = "pwsh.exe" };
        if (Exists("powershell.exe", "-Command", "exit"))
            yield return new ShellEntry { Name = "Windows PowerShell", Command = "powershell.exe" };
        if (Exists("cmd.exe", "/c", "exit"))
            yield return new ShellEntry { Name = "Command Prompt", Command = "cmd.exe" };
        if (Exists("wsl.exe", "--status"))
            yield return new ShellEntry { Name = "WSL", Command = "wsl.exe" };

        var gitBash = FindGitBash();
        if (gitBash is not null)
            yield return new ShellEntry { Name = "Git Bash", Command = gitBash };
    }

    private static bool Exists(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            return proc.WaitForExit(2000);
        }
        catch { return false; }
    }

    private static string? FindGitBash()
    {
        var candidates = new[]
        {
            @"C:\Program Files\Git\bin\bash.exe",
            @"C:\Program Files (x86)\Git\bin\bash.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Programs\Git\bin\bash.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string Basename(string command)
    {
        var first = command.Split(' ', 2)[0].Trim('"');
        return Path.GetFileName(first);
    }
}
