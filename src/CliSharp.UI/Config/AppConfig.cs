using System.Text.Json;
using System.Text.Json.Serialization;

namespace CliSharp.UI.Config;

/// <summary>
/// JSON-serializable configuration model.
/// with hot-reload.
/// </summary>
public sealed class AppConfig
{
    public FontConfig Font { get; set; } = new();
    public string Shell { get; set; } = "powershell.exe";
    public int Scrollback { get; set; } = 1000;
    public ThemeConfig Theme { get; set; } = ThemeConfig.CatppuccinMocha();
    public AiConfig Ai { get; set; } = new();
    public List<WorkflowConfig> Workflows { get; set; } =
    [
        new() { Name = "Docker Run", Command = "docker run -p {port}:80 {image}" },
        new() { Name = "Git Commit All", Command = "git add -A && git commit -m \"{message}\"" },
        new() { Name = "NPM Dev", Command = "npm run dev" },
        new() { Name = "Dotnet Watch", Command = "dotnet watch run --project {project}" },
    ];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed class FontConfig
{
    public string Family { get; set; } = "Cascadia Code,Consolas,monospace";
    public double Size { get; set; } = 14;
}

public sealed class ThemeConfig
{
    public string Name { get; set; } = "Catppuccin Mocha";
    public string Foreground { get; set; } = "#CDD6F4";
    public string Background { get; set; } = "#1E1E2E";
    public string Cursor { get; set; } = "#F5E0DC";
    public string[] Palette { get; set; } = [];

    // ── Built-in themes ─────────────────────────────────────────────

    public static ThemeConfig CatppuccinMocha() => new()
    {
        Name = "Catppuccin Mocha",
        Foreground = "#CDD6F4", Background = "#1E1E2E", Cursor = "#F5E0DC",
        Palette =
        [
            "#45475A", "#F38BA8", "#A6E3A1", "#F9E2AF",
            "#89B4FA", "#CBA6F7", "#94E2D5", "#BAC2DE",
            "#585B70", "#F38BA8", "#A6E3A1", "#F9E2AF",
            "#89B4FA", "#CBA6F7", "#94E2D5", "#CDD6F4",
        ]
    };

    public static ThemeConfig OneDark() => new()
    {
        Name = "One Dark",
        Foreground = "#ABB2BF", Background = "#282C34", Cursor = "#528BFF",
        Palette =
        [
            "#282C34", "#E06C75", "#98C379", "#E5C07B",
            "#61AFEF", "#C678DD", "#56B6C2", "#ABB2BF",
            "#5C6370", "#E06C75", "#98C379", "#E5C07B",
            "#61AFEF", "#C678DD", "#56B6C2", "#FFFFFF",
        ]
    };

    public static ThemeConfig Dracula() => new()
    {
        Name = "Dracula",
        Foreground = "#F8F8F2", Background = "#282A36", Cursor = "#F8F8F2",
        Palette =
        [
            "#21222C", "#FF5555", "#50FA7B", "#F1FA8C",
            "#BD93F9", "#FF79C6", "#8BE9FD", "#F8F8F2",
            "#6272A4", "#FF6E6E", "#69FF94", "#FFFFA5",
            "#D6ACFF", "#FF92DF", "#A4FFFF", "#FFFFFF",
        ]
    };
    public static ThemeConfig CatppuccinLatte() => new()
    {
        Name = "Catppuccin Latte",
        Foreground = "#4C4F69", Background = "#EFF1F5", Cursor = "#DC8A78",
        Palette =
        [
            "#5C5F77", "#D20F39", "#40A02B", "#DF8E1D",
            "#1E66F5", "#8839EF", "#179299", "#ACB0BE",
            "#6C6F85", "#D20F39", "#40A02B", "#DF8E1D",
            "#1E66F5", "#8839EF", "#179299", "#4C4F69",
        ]
    };

    public static ThemeConfig BuzzLightyear() => new()
    {
        Name = "Buzz Lightyear",
        Foreground = "#FDFCFC", Background = "#5F396D", Cursor = "#8BAA4E",
        Palette =
        [
            "#3D2248",  // 0  Black  (purple más oscuro)
            "#DF362A",  // 1  Red
            "#8BAA4E",  // 2  Green
            "#F1BE8C",  // 3  Yellow
            "#5F396D",  // 4  Blue   (purple principal)
            "#ADA2AD",  // 5  Magenta (gris lavanda)
            "#8BAA4E",  // 6  Cyan   (verde reutilizado)
            "#FDFCFC",  // 7  White
            "#ADA2AD",  // 8  Bright Black  (gris lavanda)
            "#DF362A",  // 9  Bright Red
            "#A4C462",  // 10 Bright Green  (verde más claro)
            "#F5D4A8",  // 11 Bright Yellow (beige más claro)
            "#7A5089",  // 12 Bright Blue   (purple más claro)
            "#C4BAC4",  // 13 Bright Magenta
            "#A4C462",  // 14 Bright Cyan
            "#FDFCFC",  // 15 Bright White
        ]
    };
}

public sealed class WorkflowConfig
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
}

public sealed class AiConfig
{
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "claude-sonnet-4-20250514";
}
