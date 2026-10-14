using System.Text.Json;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Infrastructure.Cad;
using Xunit;

namespace Infrastructure.Tests;

public sealed class CadFolderDocumentReaderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadingFailureReturnsDocumentNameAndContinuesWithIndependentDocument(bool invalidJson)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pdm_reader_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "broken.m3d"), "synthetic");
            await File.WriteAllTextAsync(Path.Combine(directory, "valid.m3d"), "synthetic");
            var result = await new CadFolderDocumentReader(new Reader(invalidJson)).ReadAsync(directory);
            var error = Assert.Single(result.Errors);
            Assert.Equal("broken.m3d", error.FileName);
            Assert.Equal(invalidJson ? "Некорректный JSON-документ." : "Документ повреждён.", error.Message);
            Assert.Equal("valid.m3d", Assert.Single(result.Documents).FileName);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private sealed class Reader(bool invalidJson) : ICadDocumentReader
    {
        public Task<CadDocument> ReadAsync(string path, CancellationToken ct = default)
        {
            if (Path.GetFileName(path) == "broken.m3d")
                throw invalidJson ? new JsonException("Invalid JSON.") : new IOException("Документ повреждён.");
            return Task.FromResult(new CadDocument(1, Path.GetFileName(path), CadDocumentType.Part,
                "АБВГ.123456.001", "Деталь", "Сталь", 2m, []));
        }
    }
}
