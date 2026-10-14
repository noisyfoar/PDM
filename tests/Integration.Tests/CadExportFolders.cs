namespace Infrastructure.Tests;

internal static class CadExportFolders
{
    public static string Find(string folderName)
    {
        var exportRoot = Environment.GetEnvironmentVariable("PDM_CAD_EXPORT_ROOT");
        if (!string.IsNullOrWhiteSpace(exportRoot))
        {
            var candidate = Path.Combine(exportRoot, folderName);
            if (!Directory.Exists(candidate)) throw new DirectoryNotFoundException(candidate);
            return candidate;
        }
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Data", "cad-export", folderName);
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"Не найден набор «{folderName}». Укажите PDM_CAD_EXPORT_ROOT.");
    }
}
