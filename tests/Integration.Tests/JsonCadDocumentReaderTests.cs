using Infrastructure.Cad;
using Xunit;

namespace Infrastructure.Tests;

[Trait("Category", "OriginalCad")]
public sealed class JsonCadDocumentReaderTests
{
    [Theory]
    [InlineData("cad-export")]
    [InlineData("cad-export-v2")]
    public async Task ReadAsync_ReadsRealCadExportFolder(string folderName)
    {
        var folder = CadExportFolders.Find(folderName);
        var folderReader = new CadFolderDocumentReader(new JsonCadDocumentReader());

        var result = await folderReader.ReadAsync(folder);

        Assert.NotEmpty(result.Documents);
        var error = Assert.Single(result.Errors);
        Assert.Equal("Отдушина.m3d", error.FileName);
        Assert.Contains(result.Documents, document => document.FileName == "Редуктор цилиндрический Ц2-160.a3d");
        Assert.Contains(result.Documents, document => document.MassKg is null);
    }


}
