namespace Cofre.App.Services;

internal static class AppPaths
{
    public static string DataDir { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cofre");

    /// <summary>Lets <c>--data-dir &lt;path&gt;</c> relocate the vault and settings (portable use, testing).</summary>
    public static void Override(string directory) => DataDir = Path.GetFullPath(directory);

    public static string VaultFile => Path.Combine(DataDir, "cofre.dat");

    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}
