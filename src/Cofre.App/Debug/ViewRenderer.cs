#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
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
                var list = (ListBox)main.FindName("Entries");
                if (list.Items.Count > 0)
                {
                    list.SelectedItem = list.Items[0];
                    main.UpdateLayout();
                    SaveMain(main, outputDir, $"{tag}-main-{section}-open");
                }
            }
            main.SelectSection("all");
            var accounts = (ListBox)main.FindName("Entries");
            accounts.SelectedItem = accounts.Items[0];
            ((Button)main.FindName("EditButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-editor");
            ((Button)main.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((Button)main.FindName("CloseDetailsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            main.Width = 1040; main.Height = 720;
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-small");
            accounts.SelectedItem = accounts.Items[0];
            main.UpdateLayout();
            SaveMain(main, outputDir, $"{tag}-main-small-open");
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
        var list = (ListBox)main.FindName("Entries");
        void SelectFirst() { list.SelectedItem = list.Items[0]; Pump(); }
        bool modalSafeDefault = false;
        Exception? modalError = null;
        void RespondToDialog(string action, string? screenshot = null, Action? before = null)
        {
            main.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var dialog = Application.Current.Windows.OfType<AppDialogWindow>().Single();
                try
                {
                    dialog.UpdateLayout();
                    modalSafeDefault = ((Button)dialog.FindName("CancelAction")).IsDefault
                        && dialog.ExtendsContentIntoTitleBar && dialog.ResizeMode == ResizeMode.NoResize;
                    if (screenshot is not null) SaveOverlay(main, (FrameworkElement)dialog.Content, screenshot, dir);
                    before?.Invoke();
                    if (action.Length > 0) ((Button)dialog.FindName(action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { modalError = ex; dialog.Close(); }
            }));
        }

        main.SelectSection("all"); Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null
            && ((ColumnDefinition)main.FindName("DetailsColumn")).Width.Value == 0, "Startup keeps details closed and returns the column space to the list");
        var bar = (Wpf.Ui.Controls.TitleBar)main.FindName("WindowTitleBar");
        Check(bar.ShowMinimize && bar.ShowMaximize && bar.ShowClose && !bar.ForceShutdown, "Caption controls are enabled and close preserves tray process");
        vault.Upsert(vault.Entries[0].Clone()); Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "A vault refresh does not select an account automatically");
        SelectFirst();
        Check(Part("DetailsPane").Visibility == Visibility.Visible && Part("ReadPanel").Visibility == Visibility.Visible
            && Part("Editor").Visibility == Visibility.Collapsed, "Explicit selection opens details in read mode");
        Click("CloseDetailsButton"); Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null
            && ((TextBlock)main.FindName("SummaryPassword")).Text == ""
            && ((Wpf.Ui.Controls.PasswordBox)main.FindName("PasswordInput")).Password == "", "Closing details clears the selection and secret fields");
        SelectFirst();
        main.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(main), 0, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "Escape closes details and clears the selection");
        SelectFirst();
        var search = (Wpf.Ui.Controls.TextBox)main.FindName("Search");
        search.Text = "no-such-account-qa";
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed, "Filtering out a selected account closes its details");
        search.Clear();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "Clearing search does not reopen an account");
        SelectFirst();
        main.SelectSection("favorites");
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "Changing sections resets account details");
        main.SelectSection("all"); SelectFirst();
        Click("EditButton");
        Check(Part("Editor").Visibility == Visibility.Visible && Part("ReadPanel").Visibility == Visibility.Collapsed, "Edit opens the form explicitly");
        Click("CancelButton");
        Check(Part("ReadPanel").Visibility == Visibility.Visible && Part("EditorActions").Visibility == Visibility.Collapsed, "Cancel returns to read mode");

        Click("EditButton");
        var persistedTitle = ((TextBlock)main.FindName("SummaryTitle")).Text;
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Edição de QA não salva";
        RespondToDialog("CancelAction", tag + "-dialog-discard");
        Click("CancelButton"); Pump();
        Check(modalError is null && modalSafeDefault && Part("Editor").Visibility == Visibility.Visible
            && ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text == "Edição de QA não salva", "The custom discard dialog defaults to keeping the edit");
        RespondToDialog("ConfirmAction");
        Click("CloseDetailsButton"); Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && vault.Entries.Any(x => x.Title == persistedTitle)
            && vault.Entries.All(x => x.Title != "Edição de QA não salva"), "Confirming discard clears the draft without changing saved data");
        Check(((Panel)main.Content).Children.OfType<Border>().All(x => x.Name != "DialogBackdrop"), "Dialog dismissal removes the background overlay");

        SelectFirst(); Click("EditButton");
        var countBeforeDelete = vault.Entries.Count;
        RespondToDialog("CancelAction", tag + "-dialog-delete");
        Click("DeleteButton"); Pump();
        Check(vault.Entries.Count == countBeforeDelete && Part("Editor").Visibility == Visibility.Visible, "Canceling deletion keeps the account and edit view");
        Click("CancelButton");
        main.ShowCopyFeedback(Part("CopyPasswordButton"), "Senha copiada");
        Pump();
        Check(((System.Windows.Controls.Primitives.Popup)main.FindName("CopyFeedback")).IsOpen, "Copy feedback is anchored beside the action");
        Click("CloseDetailsButton");
        Check(!((System.Windows.Controls.Primitives.Popup)main.FindName("CopyFeedback")).IsOpen, "Closing details also dismisses copy feedback");

        SelectFirst(); Click("EditButton");
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Edição descartada ao bloquear";
        RespondToDialog("", before: vault.Lock);
        Click("CancelButton"); Pump();
        Check(!Application.Current.Windows.OfType<AppDialogWindow>().Any() && Part("LockedOverlay").Visibility == Visibility.Visible,
            "Locking the vault cancels the custom dialog and clears the underlying view");
        vault.Unlock(password); Pump();
        using (var expiry = new CancellationTokenSource())
        {
            RespondToDialog("", tag + "-dialog-browser", expiry.Cancel);
            var accepted = AppDialogWindow.Confirm(main, "Preencher neste site?", "Confira o domínio antes de liberar suas credenciais.",
                "Permitir preenchimento", context: "Site: https://example.com\nConta: Conta de demonstração", cancellationToken: expiry.Token);
            Pump();
            Check(!accepted && !Application.Current.Windows.OfType<AppDialogWindow>().Any(), "Expiring a browser request closes its dialog without consent");
        }

        // Only executed for a newly generated demo vault, never for an existing/user vault.
        var count = vault.Entries.Count;
        Click("NewButton");
        Check(Part("Editor").Visibility == Visibility.Visible && Part("DeleteButton").Visibility == Visibility.Collapsed, "New account opens an editor without delete");
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Conta temporária de QA";
        Click("SaveButton"); Pump();
        Check(vault.Entries.Count == count + 1 && Part("ReadPanel").Visibility == Visibility.Visible, "Save persists account and returns to read mode");
        var temporary = vault.Entries.Single(x => x.Title == "Conta temporária de QA");
        Click("EditButton");
        RespondToDialog("ConfirmAction");
        Click("DeleteButton"); Pump();
        Check(vault.Entries.All(x => x.Id != temporary.Id), "Confirming the custom deletion dialog removes only the selected account");
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "Deleting a selected account does not open another account");
        SelectFirst();
        Click("EditButton");
        ((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text = "Alteração não salva";
        vault.Lock(); Pump();
        Check(Part("LockedOverlay").Visibility == Visibility.Visible, "Lock covers the workspace while retaining the caption");
        Check(((Wpf.Ui.Controls.TextBox)main.FindName("TitleInput")).Text == ""
            && ((Wpf.Ui.Controls.PasswordBox)main.FindName("PasswordInput")).Password == ""
            && ((Wpf.Ui.Controls.PasswordBox)main.FindName("TotpInput")).Password == ""
            && ((TextBlock)main.FindName("SummaryPassword")).Text == "", "Lock clears editor and summary secrets");
        vault.Unlock(password); Pump();
        Check(vault.Entries.All(x => x.Title != "Alteração não salva") && Part("DetailsPane").Visibility == Visibility.Collapsed, "Unlock restores saved data without selecting an account");
        SelectFirst();
        App.Services.Settings.Current.TrayCloseNoticeShown = true;
        main.Close();
        Check(!main.IsVisible && vault.IsUnlocked, "Closing hides the window without terminating the vault");
        main.Show(); Pump();
        Check(Part("DetailsPane").Visibility == Visibility.Collapsed && list.SelectedItem is null, "Reopening the main window starts with the account list");
        File.WriteAllLines(Path.Combine(dir, tag + "-ui-checks.txt"), checks);
    }

    private static void SaveMain(Window main, string dir, string name)
    {
        ((FrameworkElement)main.FindName("DetailsPane")).BeginAnimation(UIElement.OpacityProperty, null);
        var root = (FrameworkElement)main.Content;
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(dir, name + ".png")); encoder.Save(stream);
    }

    private static void SaveOverlay(Window owner, FrameworkElement overlay, string name, string dir)
    {
        var root = (FrameworkElement)owner.Content;
        owner.UpdateLayout(); overlay.UpdateLayout();
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            dc.DrawRectangle(new VisualBrush(overlay), null, new Rect((root.ActualWidth - overlay.ActualWidth) / 2,
                (root.ActualHeight - overlay.ActualHeight) / 2, overlay.ActualWidth, overlay.ActualHeight));
        }
        var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
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
