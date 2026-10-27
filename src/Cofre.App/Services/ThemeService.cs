using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace Cofre.App.Services;

internal static class ThemeService
{

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

        ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);
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
                SystemThemeWatcher.Watch(_watched, WindowBackdropType.None, updateAccents: false);
                _watching = true;
            }
        }
    }

    /// <summary>A neutral graphite/ivory palette with olive accents, shared by all application windows.</summary>
    private static void ApplyBrandAccent()
    {
        var dark = IsDark;
        Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
        var brand = Parse(dark ? "#BAC99A" : "#4D613B");
        ApplicationAccentColorManager.Apply(
            brand, primaryAccent: brand,
            secondaryAccent: Parse(dark ? "#CED9B5" : "#60734B"),
            tertiaryAccent: Parse(dark ? "#9FAB80" : "#3E5030"));

        var resources = Application.Current.Resources;
        void Brush(string key, string value) => resources[key] = new SolidColorBrush(Parse(value));
        Brush("BrandBrush", dark ? "#BAC99A" : "#4D613B");
        resources["BrandGradient"] = new SolidColorBrush(brand);
        Brush("MainCanvasBrush", dark ? "#1A1C18" : "#F4F3ED");
        Brush("MainSidebarBrush", dark ? "#161813" : "#EDEDE4");
        Brush("MainPanelBrush", dark ? "#20231E" : "#FBFAF5");
        Brush("MainLineBrush", dark ? "#353930" : "#D8DCD0");
        Brush("MainSelectedBrush", dark ? "#303927" : "#E2E8D5");
        Brush("MainHoverBrush", dark ? "#282D23" : "#EBEDE4");
        Brush("MainInkBrush", dark ? "#F1F1E9" : "#242A20");
        Brush("MainMutedBrush", dark ? "#A8AEA1" : "#626B5C");
        Brush("MainAccentInkBrush", dark ? "#1A2113" : "#FFFFFF");
        Brush("MainDangerBrush", dark ? "#D8A08B" : "#9A492E");
        Brush("MainDangerFillBrush", "#93472F");
        Brush("TextOnAccentFillColorPrimaryBrush", dark ? "#1A2113" : "#FFFFFF");
        Brush("TextOnAccentFillColorSecondaryBrush", dark ? "#1A2113" : "#FFFFFF");
    }

    public static bool SystemUsesDarkTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
    }

    public static bool IsDark => ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
}
