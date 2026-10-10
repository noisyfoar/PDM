using Application.Services;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using Xunit;

namespace Infrastructure.Tests;

public sealed class ReadCadFolderServiceTests
{
    [Fact]
    public async Task DelegatesFolderReadingToPort()
    {
        var expected = new CadFolderReadResult(
            [new CadDocument(1, "part.m3d", CadDocumentType.Part, "АБВГ.123456.001", "Part", "Сталь", 1m, [])],
            []);
        var reader = new FakeReader(expected);
        var useCase = new ReadCadFolderService(reader);

        var actual = await useCase.ExecuteAsync("cad-export");

        Assert.Same(expected, actual);
        Assert.Equal("cad-export", reader.LastPath);
    }

    [Fact]
    public async Task PassesCancellationTokenToPort()
    {
        var reader = new FakeReader(new CadFolderReadResult([], []));
        var useCase = new ReadCadFolderService(reader);
        using var cts = new CancellationTokenSource();

        await useCase.ExecuteAsync("cad-export", cts.Token);

        Assert.Equal(cts.Token, reader.LastToken);
    }

    private sealed class FakeReader(CadFolderReadResult result) : ICadFolderDocumentReader
    {
        public string? LastPath { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default)
        {
            LastPath = folderPath;
            LastToken = ct;
            return Task.FromResult(result);
        }
    }
}
