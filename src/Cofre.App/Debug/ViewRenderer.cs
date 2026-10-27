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
        var createdDemo = !vault.Exists;
        if (!vault.Exists)
        {
            vault.Create(masterPassword);
            vault.Upsert(new VaultEntry { Title = "E-mail pessoal", Username = "demo@example.com", Password = "senha123", Url = "https://example.com", Folder = "Pessoal", Tags = "email", Totp = "JBSWY3DPEHPK3PXP" });
            vault.Upsert(new VaultEntry { Title = "Conta de demonstração", Username = "demo", Password = "senha123", Folder = "Trabalho" });
            vault.Upsert(new VaultEntry { Title = "Serviço seguro", Username = "demo", Password = "T4!pG8#qV2$kR9&zW6", Folder = "Trabalho", Favorite = true });
        }

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
            var main = new Views.MainWindow { ShowActivated = false, Left = -20000, Top = 0, WindowStartupLocation = WindowStartupLocation.Manual };
            main.Show();
            if (createdDemo) CheckMainInteraction(main, masterPassword, outputDir, tag);
            foreach (var section in new[] { "all", "security", "totp" })
            {
                main.SelectSection(section);
                main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                main.UpdateLayout();
                SaveMain(main, outputDir, $"{tag}-main-{section}");
            }
            main.SelectSection("all");
            ((Button)main.FindName("EditButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-editor");
            ((Button)main.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            main.Width = 1040; main.Height = 720;
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-small");
            vault.Lock();
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-locked");
            main.ForceClose();
        }

        vault.Lock();
    }

    private static void CheckMainInteraction(Views.MainWindow main, string password, string dir, string tag)
    {
        var vault = App.Services.Vault;
        var checks = new List<string>();
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("UI check failed: " + description);
            checks.Add("PASS " + description);
        }
        FrameworkElement Part(string name) => (FrameworkElement)main.FindName(name);
        void Click(string name) => Part(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Pump() => main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        main.SelectSection("all"); Pump();
        Check(Part("ReadPanel").Visibility == Visibility.Visible && Part("Editor").Visibility == Visibility.Collapsed, "Selection starts in read mode");
        var bar = (Wpf.Ui.Controls.TitleBar)main.FindName("WindowTitleBar");
        Check(bar.ShowMinimize && bar.ShowMaximize && bar.ShowClose && !bar.ForceShutdown, "Caption controls are enabled and close preserves tray process");
        Click("EditButton");
        Check(Part("Editor").Visibility == Visibility.Visible && Part("ReadPanel").Visibility == Visibility.Collapsed, "Edit opens the form explicitly");
        Click("CancelButton");
        Check(Part("ReadPanel").Visibility == Visibility.Visible && Part("EditorActions").Visibility == Visibility.Collapsed, "Cancel returns to read mode");

        // Only executed for a newly generated demo vault, never for an existing/user vault.
        var count = vault.Entries.Count;
        Click("NewButton");
        Check(Part("Editor").Visibility == Visibility.Visible && Part("DeleteButton").Visibility == Visibility.Collapsed, "New account opens an editor without delete");
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Conta temporária de QA";
        Click("SaveButton"); Pump();
        Check(vault.Entries.Count == count + 1 && Part("ReadPanel").Visibility == Visibility.Visible, "Save persists account and returns to read mode");
        var temporary = vault.Entries.Single(x => x.Title == "Conta temporária de QA");
        vault.Delete(temporary.Id); Pump();

        Click("EditButton");
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Alteração não salva";
        vault.Lock(); Pump();
        Check(Part("LockedOverlay").Visibility == Visibility.Visible, "Lock covers the workspace while retaining the caption");
        Check(((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text == ""
            && ((Wpf.Ui.Controls.PasswordBox)main.FindName("PasswordInput")).Password == ""
            && ((Wpf.Ui.Controls.PasswordBox)main.FindName("TotpInput")).Password == ""
            && ((TextBlock)main.FindName("SummaryPassword")).Text == "", "Lock clears editor and summary secrets");
        vault.Unlock(password); Pump();
        Check(vault.Entries.All(x => x.Title != "Alteração não salva") && Part("ReadPanel").Visibility == Visibility.Visible, "Unlock restores saved data in read mode");
        App.Services.Settings.Current.TrayCloseNoticeShown = true;
        main.Close();
        Check(!main.IsVisible && vault.IsUnlocked, "Closing hides the window without terminating the vault");
        main.Show(); Pump();
        File.WriteAllLines(Path.Combine(dir, tag + "-ui-checks.txt"), checks);
    }

    private static void SaveMain(Window main, string dir, string name)
    {
        var root = (FrameworkElement)main.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name + ".png")); encoder.Save(stream);
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

        var backdrop = new SolidColorBrush((Color)ColorConverter.ConvertFromString(theme == "Dark" ? "#20231E" : "#FBFAF5"));

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
