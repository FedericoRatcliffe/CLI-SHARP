using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CliSharp.Application.Terminal;

namespace CliSharp.UI.Controls;

/// <summary>
/// Reusable overlay for command palette and fuzzy history search.
/// Search TextBox at top, results list below with keyboard nav.
/// </summary>
public sealed class PaletteOverlay : Panel
{
    public record Item(string Label, string? Shortcut, Action? Execute);

    private readonly TextBox _searchBox;
    private readonly StackPanel _resultsList;
    private List<Item> _allItems;
    private List<Item> _filteredItems;
    private int _selectedIndex;

    public event Action? Dismissed;

    public PaletteOverlay(List<Item> items, string placeholder = "Type a command...")
    {
        _allItems = items;
        _filteredItems = items.ToList();

        // Semi-transparent background
        Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0));

        _searchBox = new TextBox
        {
            Watermark = placeholder,
            FontSize = 14,
            Padding = new Thickness(10, 8),
        };

        _resultsList = new StackPanel { Spacing = 2 };

        var scroller = new ScrollViewer
        {
            Content = _resultsList,
            MaxHeight = 320,
        };

        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#1E1E2E")),
            BorderBrush = new SolidColorBrush(Color.Parse("#45475A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Width = 520,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 60, 0, 0),
            Child = new StackPanel
            {
                Spacing = 6,
                Children = { _searchBox, scroller },
            },
        };

        Children.Add(card);

        _searchBox.KeyDown += OnSearchKeyDown;
        _searchBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                FilterItems(_searchBox.Text ?? "");
        };

        // Click on background to close
        PointerPressed += (_, e) => { if (e.Source == this) Close(); };

        UpdateResults();
    }

    public void FocusSearch() => _searchBox.Focus();

    private void Close() => Dismissed?.Invoke();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                _selectedIndex = Math.Min(_selectedIndex + 1, _filteredItems.Count - 1);
                UpdateResults(); e.Handled = true; break;
            case Key.Up:
                _selectedIndex = Math.Max(_selectedIndex - 1, 0);
                UpdateResults(); e.Handled = true; break;
            case Key.Enter:
                if (_selectedIndex >= 0 && _selectedIndex < _filteredItems.Count)
                    _filteredItems[_selectedIndex].Execute?.Invoke();
                Close(); e.Handled = true; break;
            case Key.Escape:
                Close(); e.Handled = true; break;
        }
    }

    private void FilterItems(string query)
    {
        if (string.IsNullOrEmpty(query))
            _filteredItems = _allItems.ToList();
        else
            _filteredItems = _allItems
                .Select(item => (item, score: CommandHistory.FuzzyScore(query, item.Label)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .Select(x => x.item)
                .Take(20)
                .ToList();

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _filteredItems.Count - 1));
        UpdateResults();
    }

    private void UpdateResults()
    {
        _resultsList.Children.Clear();
        for (int i = 0; i < _filteredItems.Count; i++)
        {
            int idx = i;
            var item = _filteredItems[i];
            bool isSel = i == _selectedIndex;

            var row = new DockPanel
            {
                Background = isSel
                    ? new SolidColorBrush(Color.Parse("#313244"))
                    : Brushes.Transparent,
                Margin = new Thickness(0),
            };

            if (item.Shortcut is not null)
            {
                var shortcutText = new TextBlock
                {
                    Text = item.Shortcut,
                    Foreground = new SolidColorBrush(Color.Parse("#6C7086")),
                    FontSize = 11,
                    Padding = new Thickness(8, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                DockPanel.SetDock(shortcutText, Dock.Right);
                row.Children.Add(shortcutText);
            }

            row.Children.Add(new TextBlock
            {
                Text = item.Label,
                Foreground = new SolidColorBrush(isSel ? Color.Parse("#CDD6F4") : Color.Parse("#BAC2DE")),
                FontSize = 13,
                Padding = new Thickness(8, 5),
                VerticalAlignment = VerticalAlignment.Center,
            });

            row.PointerPressed += (_, _) =>
            {
                _filteredItems[idx].Execute?.Invoke();
                Close();
            };

            _resultsList.Children.Add(row);
        }
    }
}
