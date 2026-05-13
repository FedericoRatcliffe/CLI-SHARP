using System.IO;
using System.Text.Json;

namespace SharpTerminal.UI.Config;

/// <summary>
/// Loads, saves, and watches config.json with hot-reload.
/// Ubicación: ~/.SharpTerminal/config.json
/// </summary>
public sealed class ConfigManager : IDisposable
{
    private readonly string _configPath;
    private FileSystemWatcher? _watcher;
    private Timer? _debounce;

    public AppConfig Config { get; private set; } = new();
    public event Action<AppConfig>? ConfigChanged;

    public ConfigManager()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".SharpTerminal");
        Directory.CreateDirectory(dir);
        _configPath = Path.Combine(dir, "config.json");

        Config = Load();
        StartWatching(dir);
    }

    private AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<AppConfig>(json, AppConfig.JsonOptions) ?? new AppConfig();
            }
        }
        catch { }

        // Create default config if not found
        var defaultConfig = new AppConfig();
        Save(defaultConfig);
        return defaultConfig;
    }

    private void Save(AppConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, AppConfig.JsonOptions);
            File.WriteAllText(_configPath, json);
        }
        catch { }
    }

    private void StartWatching(string dir)
    {
        try
        {
            _watcher = new FileSystemWatcher(dir, "config.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += (_, _) =>
            {
                _debounce?.Dispose();
                _debounce = new Timer(_ =>
                {
                    try
                    {
                        Config = Load();
                        ConfigChanged?.Invoke(Config);
                    }
                    catch { }
                }, null, 300, Timeout.Infinite);
            };
        }
        catch { }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounce?.Dispose();
    }
}
