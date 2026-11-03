using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using SysmlStudio.App.Services;
using SysmlStudio.App.ViewModels;

namespace SysmlStudio.App.Views;

/// <summary>
/// The one window. It owns the pickers and the keyboard: its key bindings are
/// made from the shortcuts in force, and made again when the user changes them.
/// </summary>
public sealed partial class MainWindow : Window, IShellDialogs
{
    private ShellViewModel? _shell;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ShellViewModel);
    }

    public async Task<string?> PickFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a folder of .sysml files",
            AllowMultiple = false,
        });

        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export as {extension.ToUpperInvariant()}",
            SuggestedFileName = suggestedName + "." + extension,
            DefaultExtension = extension,
        });

        return file?.TryGetLocalPath();
    }

    private void Attach(ShellViewModel? shell)
    {
        if (_shell is not null)
        {
            _shell.Keys.Changed -= BindKeys;
            _shell.SearchFocusRequested -= FocusSearch;
            _shell.KindListRequested -= OpenKindList;
        }

        _shell = shell;
        if (shell is not null)
        {
            shell.Keys.Changed += BindKeys;
            shell.SearchFocusRequested += FocusSearch;
            shell.KindListRequested += OpenKindList;
        }

        BindKeys();
    }

    /// <summary>Opens the switcher's list, the way a click on its button does.</summary>
    private void OpenKindList()
    {
        if (_shell is { ShowsKinds: true })
            KindListButton.Flyout?.ShowAt(KindListButton);
    }

    /// <summary>The list has opened: the keyboard starts on the kind in front, or on the first one on offer.</summary>
    private static void OnKindListOpened(object? sender, EventArgs e)
    {
        if (sender is not Flyout { Content: Control content })
            return;

        var options = KindOptionButtons(content);
        var start = options.Find(b => b.Classes.Contains("active")) ?? options.FirstOrDefault();
        start?.Focus(NavigationMethod.Directional);
    }

    /// <summary>The arrow keys walk the kinds on offer, column by column.</summary>
    private static void OnKindListKeyDown(object? sender, KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Key.Down or Key.Right => 1,
            Key.Up or Key.Left => -1,
            _ => 0,
        };
        if (step == 0 || sender is not Control content)
            return;

        var options = KindOptionButtons(content);
        if (options.Count == 0)
            return;

        var at = options.FindIndex(b => b.IsFocused);
        var next = at < 0 ? 0 : (at + step + options.Count) % options.Count;
        options[next].Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    private static List<Button> KindOptionButtons(Control content)
        => [.. content.GetVisualDescendants().OfType<Button>().Where(b => b.IsEnabled && b.Classes.Contains("kind-option"))];

    /// <summary>A kind was picked from the list: draw it, and put the list away.</summary>
    private void OnKindOptionClick(object? sender, RoutedEventArgs e)
    {
        // Read first: a button leaves its data context behind when the list closes.
        var option = (sender as Control)?.DataContext as DiagramKindOption;
        KindListButton.Flyout?.Hide();
        if (option is not null)
            _shell?.SwitchKindCommand.Execute(option);
    }

    private void BindKeys()
    {
        KeyBindings.Clear();
        if (_shell is not { } shell)
            return;

        foreach (var action in ShortcutCatalog.All)
        {
            if (shell.Keys.KeyGesture(action.Id) is { } gesture)
                KeyBindings.Add(new KeyBinding { Gesture = gesture, Command = new RelayCommand(() => shell.RunShortcut(action)) });
        }
    }

    private void FocusSearch()
    {
        if (_shell is { HasWorkspace: false })
            return;

        if (_shell?.ShowsSearch == true)
            SideSearch.Focus();
        else
            SearchBox.Focus();
    }
}
