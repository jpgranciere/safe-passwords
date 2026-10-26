using System.Windows;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace Cofre.App.Services;

/// <summary>The notification-area icon: left click toggles the popup, right click opens a small menu.</summary>
internal sealed class TrayService : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Drawing.Icon _lockedIcon = LoadIcon("cofre.ico");
    private readonly Drawing.Icon _unlockedIcon = LoadIcon("cofre-open.ico");
    private readonly WinForms.ToolStripMenuItem _lockItem;

    public TrayService()
    {
        var menu = new WinForms.ContextMenuStrip { ShowImageMargin = false };
        var open = new WinForms.ToolStripMenuItem("Abrir cofre") { Font = new Drawing.Font(menu.Font, Drawing.FontStyle.Bold) };
        open.Click += (_, _) => ToggleRequested?.Invoke();
        _lockItem = new WinForms.ToolStripMenuItem("Bloquear agora");
        _lockItem.Click += (_, _) => LockRequested?.Invoke();
        var exit = new WinForms.ToolStripMenuItem("Sair");
        exit.Click += (_, _) => ExitRequested?.Invoke();
        menu.Items.AddRange([open, _lockItem, new WinForms.ToolStripSeparator(), exit]);
        ApplyMenuTheme(menu);

        _icon = new WinForms.NotifyIcon
        {
            Icon = _lockedIcon,
            Text = "Cofre (bloqueado)",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) ToggleRequested?.Invoke();
        };
    }

    public event Action? ToggleRequested;
    public event Action? LockRequested;
    public event Action? ExitRequested;

    public void SetUnlocked(bool unlocked)
    {
        _icon.Icon = unlocked ? _unlockedIcon : _lockedIcon;
        _icon.Text = unlocked ? "Cofre (desbloqueado)" : "Cofre (bloqueado)";
        _lockItem.Enabled = unlocked;
    }

    public void ShowMessage(string title, string text) =>
        _icon.ShowBalloonTip(3000, title, text, WinForms.ToolTipIcon.Info);

    private static Drawing.Icon LoadIcon(string name)
    {
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}", UriKind.Absolute))
                       ?? throw new FileNotFoundException($"Recurso {name} não encontrado.");
        using var stream = resource.Stream;
        return new Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
    }

    private static void ApplyMenuTheme(WinForms.ContextMenuStrip menu)
    {
        var dark = ThemeService.SystemUsesDarkTheme();
        menu.Renderer = new WinForms.ToolStripProfessionalRenderer(new MenuColors(dark)) { RoundedEdges = false };
        menu.BackColor = dark ? Drawing.Color.FromArgb(44, 44, 48) : Drawing.Color.White;
        menu.ForeColor = dark ? Drawing.Color.White : Drawing.Color.FromArgb(30, 30, 30);
        menu.Font = new Drawing.Font("Segoe UI", 9.5f);
        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            item.ForeColor = menu.ForeColor;
            item.Padding = new WinForms.Padding(4, 5, 4, 5);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _lockedIcon.Dispose();
        _unlockedIcon.Dispose();
    }

    private sealed class MenuColors(bool dark) : WinForms.ProfessionalColorTable
    {
        private readonly Drawing.Color _background = dark ? Drawing.Color.FromArgb(44, 44, 48) : Drawing.Color.White;
        private readonly Drawing.Color _hover = dark ? Drawing.Color.FromArgb(66, 66, 74) : Drawing.Color.FromArgb(236, 236, 242);
        private readonly Drawing.Color _border = dark ? Drawing.Color.FromArgb(72, 72, 80) : Drawing.Color.FromArgb(214, 214, 220);

        public override Drawing.Color ToolStripDropDownBackground => _background;
        public override Drawing.Color ImageMarginGradientBegin => _background;
        public override Drawing.Color ImageMarginGradientMiddle => _background;
        public override Drawing.Color ImageMarginGradientEnd => _background;
        public override Drawing.Color MenuBorder => _border;
        public override Drawing.Color MenuItemBorder => _hover;
        public override Drawing.Color MenuItemSelected => _hover;
        public override Drawing.Color MenuItemSelectedGradientBegin => _hover;
        public override Drawing.Color MenuItemSelectedGradientEnd => _hover;
        public override Drawing.Color MenuItemPressedGradientBegin => _hover;
        public override Drawing.Color MenuItemPressedGradientMiddle => _hover;
        public override Drawing.Color MenuItemPressedGradientEnd => _hover;
        public override Drawing.Color SeparatorDark => _border;
        public override Drawing.Color SeparatorLight => _background;
    }
}
