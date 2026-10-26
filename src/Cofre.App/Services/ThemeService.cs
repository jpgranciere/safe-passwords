using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cofre.App.Services;

internal static class ThemeService
{
    private static readonly System.Windows.Media.Color Brand = System.Windows.Media.Color.FromRgb(0x7C, 0x5C, 0xFF);

    private static Window? _watched;
    private static bool _hooked;
    private static bool _watching;

    /// <summary>Applies System/Dark/Light with the brand accent. Pass the popup so "System" can follow Windows live.</summary>
    public static void Apply(string mode, Window? window = null)
    {
        if (window is not null) _watched = window;

        var theme = mode switch
        {
            "Dark" => ApplicationTheme.Dark,
            "Light" => ApplicationTheme.Light,
            _ => SystemUsesDarkTheme() ? ApplicationTheme.Dark : ApplicationTheme.Light,
        };

        ApplicationThemeManager.Apply(theme, WindowBackdropType.Acrylic, updateAccent: false);
        ApplyBrandAccent();

        if (!_hooked)
        {
            _hooked = true;
            // When Windows flips between light and dark the library re-themes the controls; keep our accent on top.
            ApplicationThemeManager.Changed += (_, _) => ApplyBrandAccent();
        }

        if (_watched is not null)
        {
            // UnWatch throws for a window the watcher never saw, so only undo what we did ourselves.
            if (_watching)
            {
                SystemThemeWatcher.UnWatch(_watched);
                _watching = false;
            }

            if (mode is not ("Dark" or "Light"))
            {
                SystemThemeWatcher.Watch(_watched, WindowBackdropType.Acrylic, updateAccents: false);
                _watching = true;
            }
        }
    }

    /// <summary>The library derives a pale accent in dark mode; the brand colour is used as-is (hover lighter, pressed darker).</summary>
    private static void ApplyBrandAccent()
    {
        ApplicationAccentColorManager.Apply(
            Brand,
            primaryAccent: Brand,
            secondaryAccent: System.Windows.Media.Color.FromRgb(0x8D, 0x71, 0xFF),
            tertiaryAccent: System.Windows.Media.Color.FromRgb(0x6A, 0x49, 0xF0));

        // Dark mode would otherwise put black text on the accent; the brand purple wants white.
        var resources = Application.Current.Resources;
        resources["TextOnAccentFillColorPrimaryBrush"] = new SolidColorBrush(Colors.White);
        resources["TextOnAccentFillColorSecondaryBrush"] = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));
    }

    public static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
}
