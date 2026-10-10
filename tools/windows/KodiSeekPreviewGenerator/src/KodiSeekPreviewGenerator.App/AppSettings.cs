using System.Text.Json;

namespace KodiSeekPreviewGenerator;

internal static class AppSettings
{
    private sealed record SettingsDocument(string? LastFolder);

    private static readonly string SettingsFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KodiSeekPreviewGenerator");
    private static readonly string SettingsPath = Path.Combine(SettingsFolder, "settings.json");

    public static string? LoadLastFolder()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return null;
            SettingsDocument? settings = JsonSerializer.Deserialize<SettingsDocument>(
                File.ReadAllText(SettingsPath));
            return settings?.LastFolder;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveLastFolder(string folder)
    {
        Directory.CreateDirectory(SettingsFolder);
        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(new SettingsDocument(folder), new JsonSerializerOptions
            {
                WriteIndented = true,
            }));
    }
}
