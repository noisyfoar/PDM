using Infrastructure.Cad;
using Xunit;

namespace Infrastructure.Tests;

public sealed class JsonCadReaderContractTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UsesDiskFileNameAndAllowsAssemblyWithoutProperties(bool includeJsonName)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pdm_reader_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "actual.a3d");
        try
        {
            var jsonName = includeJsonName ? "\"fileName\":\"wrong.a3d\"," : "";
            await File.WriteAllTextAsync(file, "{" + jsonName +
                "\"formatVersion\":1,\"type\":\"Assembly\",\"designation\":\"АБВГ.123456.001\",\"name\":\"Сборка\",\"components\":[]}");
            var document = await new JsonCadDocumentReader().ReadAsync(file);
            Assert.Equal("actual.a3d", document.FileName);
            Assert.Null(document.Material);
            Assert.Null(document.MassKg);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
