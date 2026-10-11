using Infrastructure.Cad;
using Xunit;

namespace Infrastructure.Tests;

public sealed class JsonCadDocumentReaderTests
{
    [Theory]
    [InlineData("cad-export")]
    [InlineData("cad-export-v2")]
    public async Task ReadAsync_ReadsRealCadExportFolder(string folderName)
    {
        var folder = FindProjectFolder("data", "cad-export", folderName);
        var folderReader = new CadFolderDocumentReader(new JsonCadDocumentReader());

        var result = await folderReader.ReadAsync(folder);

        Assert.NotEmpty(result.Documents);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Отдушина.m3d", Path.GetFileName(error.Path));
        Assert.Contains(result.Documents, document => document.FileName == "Редуктор цилиндрический Ц2-160.a3d");
        Assert.Contains(result.Documents, document => document.MassKg is null);
    }


    private static string FindProjectFolder(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Не найден каталог с тестовыми CAD-файлами: {Path.Combine(parts)}");
    }
}
