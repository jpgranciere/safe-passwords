#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Cofre.App.Services;
using Cofre.App.Views;
using Cofre.Core.Models;

namespace Cofre.App.Debug;

/// <summary>
/// Debug-only: <c>Cofre.exe --render &lt;dir&gt; --password &lt;master&gt; --data-dir &lt;dir&gt;</c> draws every screen of the real
/// popup window to a PNG (dark and light). The window is parked far off-screen, so nothing is ever shown on a monitor
/// and no keystrokes or screen capture are involved.
/// </summary>
internal static class ViewRenderer
{
    public static void RenderAll(PopupWindow popup, string outputDir, string masterPassword)
    {
        Directory.CreateDirectory(outputDir);
        var vault = App.Services.Vault;

        popup.ShowActivated = false;
        popup.Left = -20000;
        popup.Top = 0;

        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeService.Apply(theme);
            var tag = theme.ToLowerInvariant();

            vault.Lock();
            Save(popup, new SetupView(popup), outputDir, $"{tag}-1-setup", theme, 584);
            Save(popup, new UnlockView(popup), outputDir, $"{tag}-2-unlock", theme, 584);

            vault.Unlock(masterPassword);
            var entries = vault.Entries;

            Save(popup, new VaultView(popup), outputDir, $"{tag}-3-list", theme, 584);
            Save(popup, new EntryEditorView(popup, entries[0].Clone(), isNew: false), outputDir, $"{tag}-4-edit", theme, 584);
            Save(popup, new EntryEditorView(popup, new VaultEntry(), isNew: true), outputDir, $"{tag}-5-new", theme, 584);
            Save(popup, new GeneratorView(popup, onUse: _ => { }, onBack: () => { }), outputDir, $"{tag}-6-generator", theme, 584);
            Save(popup, new SettingsView(popup), outputDir, $"{tag}-7-settings", theme, 860);
            Save(popup, new ChangePasswordView(popup), outputDir, $"{tag}-8-change-password", theme, 584);
        }

        vault.Lock();
    }

    private static void Save(PopupWindow popup, UserControl view, string dir, string name, string theme, double height)
    {
        popup.MinHeight = height;
        popup.Height = height;
        popup.Navigate(view);
        view.BeginAnimation(UIElement.OpacityProperty, null); // skip the fade-in
        view.Opacity = 1;
        if (!popup.IsVisible) popup.Show();

        popup.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); // Loaded, templates, list containers
        popup.UpdateLayout();
        popup.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var root = (FrameworkElement)popup.Content;
        double width = root.ActualWidth, h = root.ActualHeight;

        // Stand-in for the acrylic backdrop, which DWM draws outside of WPF and so isn't part of the render.
        var backdrop = new SolidColorBrush(theme == "Dark" ? Color.FromRgb(0x2B, 0x2B, 0x31) : Color.FromRgb(0xEE, 0xEE, 0xF4));

        const double scale = 2;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawRectangle(backdrop, null, new Rect(0, 0, width, h));
            // Without an explicit viewbox the brush would crop to the children's bounds and drop the margins.
            var area = new Rect(0, 0, width, h);
            dc.DrawRectangle(new VisualBrush(root)
            {
                ViewboxUnits = BrushMappingMode.Absolute,
                Viewbox = area,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = area,
            }, null, area);
        }

        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(h * scale), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name + ".png"));
        encoder.Save(stream);
    }
}
#endif
