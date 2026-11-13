using System.Collections.ObjectModel;
using System.Linq;
using Astrid.Core.Bindings;

namespace Astrid.App.ViewModels;

/// <summary>
/// The Appearance page's own two settings: the look, and the global quick-add chord. Both are this
/// machine's rather than the account's — a laptop in the evening and a desk under an office light
/// are different questions. (The page's task-layout, subtask and smart-parsing controls are the
/// account's, and read <see cref="TaskSettingsViewModel"/>.)
/// </summary>
public sealed class AppearanceSettingsViewModel : ObservableObject
{
    private readonly SettingsSession _session;
    private string _theme = "ocean";
    private bool? _themeIsDark = false;
    private string _titleDisplay = "wrap";
    private int _titleMaxLines;
    private Hotkey _hotkey = new();

    public AppearanceSettingsViewModel(SettingsSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Which look the app wears: <c>ocean</c>, <c>light</c>, <c>dark</c> or <c>auto</c>.
    /// </summary>
    /// <remarks>
    /// Ocean is the brand look and the default — a light appearance with a cyan surface — so an
    /// app that has never been configured is wearing it. See <c>astrid_core::theme</c>.
    /// </remarks>
    public string Theme
    {
        get => _theme;
        private set
        {
            Set(ref _theme, value);
            Raise(nameof(ThemeChoice));
        }
    }

    /// <summary>What the picker shows. The same list on every client, in the core's order.</summary>
    public ObservableCollection<string> ThemeChoices { get; } = [];

    /// <summary>The chosen entry, for a two-way picker.</summary>
    public string ThemeChoice
    {
        get => Theme;
        set
        {
            if (!string.IsNullOrEmpty(value) && value != Theme)
            {
                _ = SetThemeAsync(value);
            }
        }
    }

    /// <summary>
    /// Whether this look draws dark, or leaves it to the system.
    /// </summary>
    /// <remarks>
    /// Null for <c>auto</c>. The window uses it to decide between an explicit appearance and
    /// following Windows — a shell that guessed would pick one and be wrong half the time.
    /// </remarks>
    public bool? ThemeIsDark
    {
        get => _themeIsDark;
        private set => Set(ref _themeIsDark, value);
    }

    /// <summary>
    /// How much of a title a row shows: <c>wrap</c>, <c>twoLines</c> or <c>oneLine</c> (DS19).
    /// </summary>
    /// <remarks>
    /// This machine's, like <see cref="Theme"/> and through the same door — the core's cache, never
    /// the Outbox. See <c>astrid_core::title_display</c>.
    /// </remarks>
    public string TitleDisplay
    {
        get => _titleDisplay;
        private set
        {
            Set(ref _titleDisplay, value);
            Raise(nameof(TitleDisplayChoice));
        }
    }

    /// <summary>
    /// What the picker shows. The same three on every client, in the core's order, each carrying
    /// the <c>.resw</c> key for its words — the theme's picker above shows its wire names, which is
    /// a gap of its own rather than a pattern to copy.
    /// </summary>
    public ObservableCollection<DefaultChoice> TitleDisplayChoices { get; } = [];

    /// <summary>The chosen entry, for the picker.</summary>
    public DefaultChoice? TitleDisplayChoice =>
        TitleDisplayChoices.FirstOrDefault(choice => choice.Value == TitleDisplay);

    /// <summary>
    /// How many lines a row's title may use — <c>0</c> for as many as it needs.
    /// </summary>
    /// <remarks>
    /// Zero because that is what WinUI's <c>TextBlock.MaxLines</c> means by "do not limit it"; the
    /// core says <c>null</c>, and this is the one place the two spellings meet. The row binds this
    /// rather than matching on the names, so adding a fourth option is a core change alone.
    /// </remarks>
    public int TitleMaxLines
    {
        get => _titleMaxLines;
        private set => Set(ref _titleMaxLines, value);
    }

    /// <summary>Raised when the look changes, so the window can repaint itself.</summary>
    public event Action? ThemeChanged;

    /// <summary>Raised when the title setting changes, so open lists can re-measure their rows.</summary>
    public event Action? TitleDisplayChanged;

    /// <summary>Raised when the global chord changes, so the window can register the new one.</summary>
    public event Action<Hotkey>? HotkeyChanged;

    /// <summary>The global quick-add chord, as the core has it — chosen, or as shipped.</summary>
    public Hotkey Hotkey
    {
        get => _hotkey;
        private set
        {
            if (Set(ref _hotkey, value))
            {
                Raise(nameof(HotkeyChord));
            }
        }
    }

    /// <summary>The chord as words — <c>Ctrl+Shift+A</c> — for the box that edits it.</summary>
    public string HotkeyChord => _hotkey.Chord;

    public async Task LoadHotkeyAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.Hotkey(), cancellationToken);
        if (response.Ok && response.Read<Hotkey>() is { } hotkey)
        {
            Hotkey = hotkey;
        }
    }

    /// <summary>
    /// Choose another chord. The core decides whether it is one — a modifier, one key — and says
    /// why not; a chord it accepts is registered by the window at once.
    /// </summary>
    public async Task<bool> SetHotkeyAsync(string chord, CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.SetHotkey(chord), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        _session.ErrorMessage = null;
        if (response.Read<Hotkey>() is { } hotkey)
        {
            Hotkey = hotkey;
            HotkeyChanged?.Invoke(hotkey);
        }
        return true;
    }

    /// <summary>Read back which look this installation is set to.</summary>
    public async Task LoadThemeAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.Theme(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        Apply(response);
        ThemeChoices.Clear();
        if (response.Value.TryGetProperty("choices", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.GetString() is { } name)
                {
                    ThemeChoices.Add(name);
                }
            }
        }
        ThemeChanged?.Invoke();
    }

    /// <summary>Choose a look.</summary>
    public async Task<bool> SetThemeAsync(string theme,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.SetTheme(theme), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        Apply(response);
        ThemeChanged?.Invoke();
        return true;
    }

    /// <summary>Read back how much of a title this installation shows.</summary>
    public async Task LoadTitleDisplayAsync(CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(Commands.TitleDisplay(), cancellationToken);
        if (!response.Ok)
        {
            return;
        }
        ApplyTitleDisplay(response);
        TitleDisplayChoices.Clear();
        if (response.Value.TryGetProperty("choices", out var choices))
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.GetString() is { } name)
                {
                    // The words come from the strings table, keyed by the core's name, so the
                    // three options are translated rather than shown as wire values.
                    TitleDisplayChoices.Add(
                        new DefaultChoice("titleDisplay", name, $"titles.{name}", null, false));
                }
            }
        }
        TitleDisplayChanged?.Invoke();
    }

    /// <summary>Choose how much of a title a row shows.</summary>
    public async Task<bool> SetTitleDisplayAsync(string display,
        CancellationToken cancellationToken = default)
    {
        var response = await _session.Core.CallAsync(
            Commands.SetTitleDisplay(display), cancellationToken);
        if (!response.Ok)
        {
            _session.ErrorMessage = response.Error?.Message;
            return false;
        }
        ApplyTitleDisplay(response);
        TitleDisplayChanged?.Invoke();
        return true;
    }

    private void ApplyTitleDisplay(AstridResponse response)
    {
        if (response.Value.TryGetProperty("titleDisplay", out var display)
            && display.GetString() is { } name)
        {
            TitleDisplay = name;
        }
        // The core says null for "as many lines as it needs"; WinUI says 0. Translated here, once,
        // rather than in the row — a converter per list would be the same rule in two places.
        TitleMaxLines = response.Value.TryGetProperty("maxLines", out var lines)
            && lines.ValueKind is System.Text.Json.JsonValueKind.Number
            ? lines.GetInt32()
            : 0;
    }

    private void Apply(AstridResponse response)
    {
        if (response.Value.TryGetProperty("theme", out var theme) && theme.GetString() is { } name)
        {
            Theme = name;
        }
        ThemeIsDark = response.Value.TryGetProperty("isDark", out var dark)
            && dark.ValueKind is System.Text.Json.JsonValueKind.True
                or System.Text.Json.JsonValueKind.False
            ? dark.GetBoolean()
            : null;
    }
}
