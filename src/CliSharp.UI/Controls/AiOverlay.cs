using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CliSharp.UI.AI;

namespace CliSharp.UI.Controls;

/// <summary>
/// Overlay for AI interaction: input → respuesta → Execute/Copy.
/// Reusable for generate, explain, debug, and summarize.
/// </summary>
public sealed class AiOverlay : Panel
{
    private readonly TextBox _inputBox;
    private readonly TextBlock _responseText;
    private readonly StackPanel _actionButtons;
    private readonly AiService _ai;
    private readonly string _systemPrompt;
    private string? _generatedCommand;
    private CancellationTokenSource? _cts;

    public event Action? Dismissed;
    public event Action<string>? ExecuteCommand;
    public event Action<string>? CopyToClipboard;

    /// <summary>
    /// Creates the overlay. If prefill is not null, it executes immediately.
    /// </summary>
    public AiOverlay(AiService ai, string systemPrompt, string placeholder,
                     string? prefill = null, string? prefillContext = null)
    {
        _ai = ai;
        _systemPrompt = systemPrompt;

        Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));

        _inputBox = new TextBox
        {
            Watermark = placeholder,
            FontSize = 14,
            Padding = new Thickness(10, 8),
            AcceptsReturn = false,
        };

        _responseText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#CDD6F4")),
            FontSize = 13,
            Padding = new Thickness(8),
            IsVisible = false,
        };

        _actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 6, 0, 0),
            IsVisible = false,
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#89B4FA")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Width = 560,
            MaxHeight = 420,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "\u2728 AI Assistant",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.Parse("#89B4FA")),
                        FontWeight = FontWeight.Bold,
                    },
                    _inputBox,
                    new ScrollViewer { Content = _responseText, MaxHeight = 260 },
                    _actionButtons,
                }
            },
        };

        Children.Add(card);

        _inputBox.KeyDown += OnInputKeyDown;
        PointerPressed += (_, e) => { if (e.Source == this) Close(); };

        // Pre-fill: Execute directo sin input del usuario
        if (prefill is not null)
        {
            _inputBox.Text = prefill;
            _inputBox.IsReadOnly = true;
            if (prefillContext is not null)
                _ = SendQueryAsync($"{prefill}\n\n{prefillContext}");
            else
                _ = SendQueryAsync(prefill);
        }
    }

    public void FocusInput() => _inputBox.Focus();

    private void Close()
    {
        _cts?.Cancel();
        Dismissed?.Invoke();
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_inputBox.Text))
        {
            _ = SendQueryAsync(_inputBox.Text!);
            e.Handled = true;
        }
    }

    private async Task SendQueryAsync(string message)
    {
        _responseText.Text = "Thinking...";
        _responseText.Foreground = new SolidColorBrush(Color.Parse("#6C7086"));
        _responseText.IsVisible = true;
        _actionButtons.IsVisible = false;
        _inputBox.IsReadOnly = true;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        var response = await _ai.AskAsync(_systemPrompt, message, _cts.Token);

        _responseText.Text = response;
        _responseText.Foreground = new SolidColorBrush(Color.Parse("#CDD6F4"));

        // Detectar si la respuesta es un comando ejecutable (1 línea, sin bullets/explicación)
        var trimmed = response.Trim();
        bool looksLikeCommand = !trimmed.Contains('\n') && !trimmed.StartsWith('-')
                                && !trimmed.StartsWith('[') && trimmed.Length < 300;

        _actionButtons.Children.Clear();

        if (looksLikeCommand && _systemPrompt == AiPrompts.GenerateCommand)
        {
            _generatedCommand = trimmed;
            _actionButtons.Children.Add(CreateButton("\u25b6 Execute", "#A6E3A1", () =>
            {
                ExecuteCommand?.Invoke(_generatedCommand);
                Close();
            }));
        }

        _actionButtons.Children.Add(CreateButton("\ud83d\udccb Copy", "#89B4FA", () =>
        {
            CopyToClipboard?.Invoke(response);
        }));

        _actionButtons.Children.Add(CreateButton("Close", "#6C7086", Close));
        _actionButtons.IsVisible = true;
    }

    private static Button CreateButton(string text, string color, Action onClick)
    {
        var btn = new Button
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(12, 4),
            Foreground = new SolidColorBrush(Color.Parse(color)),
            Background = new SolidColorBrush(Color.Parse("#313244")),
        };
        btn.Click += (_, _) => onClick();
        return btn;
    }
}
