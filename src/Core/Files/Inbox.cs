using System.Diagnostics;
using System.Runtime.Versioning;
using Core.Config;

namespace Core.Files;

/// <summary>
/// Receives files uploaded from a client into one inbox folder under the user's
/// Downloads. Names are sanitised (no path traversal) and de-duplicated.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Inbox
{
    public static async Task<string> SaveAsync(Stream body, string suggestedName)
    {
        Directory.CreateDirectory(AppPaths.Inbox);
        string name = MakeSafeName(suggestedName);
        string path = UniquePath(Path.Combine(AppPaths.Inbox, name));

        await using var fileStream = File.Create(path);
        await body.CopyToAsync(fileStream);
        return path;
    }

    public static void OpenFolder()
    {
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Inbox}\"") { UseShellExecute = true });
    }

    private static string MakeSafeName(string name)
    {
        name = Path.GetFileName(name ?? "");          // strip any directory part
        if (string.IsNullOrWhiteSpace(name)) name = "upload";
        foreach (var invalidChar in Path.GetInvalidFileNameChars()) name = name.Replace(invalidChar, '_');
        return name;
    }

    private static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        string dir = Path.GetDirectoryName(path)!;
        string baseName = Path.GetFileNameWithoutExtension(path);
        string ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string candidate = Path.Combine(dir, $"{baseName} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
