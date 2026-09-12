using System.Collections.ObjectModel;
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

    /// <summary>Raised when the look changes, so the window can repaint itself.</summary>
    public event Action? ThemeChanged;

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
