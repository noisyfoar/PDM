using System.IO;
using System.Text.Json;
using Shell.Configuration;
using Xunit;

namespace Shell.Tests;

public sealed class AppSettingsTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"pdm_settings_test_{Guid.NewGuid():N}");
    private string SettingsPath => Path.Combine(_directory, "appsettings.json");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }
    public Task DisposeAsync()
    {
        File.Delete(SettingsPath);
        Directory.Delete(_directory);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task LoadsConnectionStringWithoutChangingItsValue()
    {
        const string connectionString = "Host=localhost;Database=pdm;Username=admin;Password=  password  ";
        await File.WriteAllTextAsync(SettingsPath, JsonSerializer.Serialize(new
        {
            ConnectionStrings = new { Pdm = connectionString }
        }));
        var settings = await AppSettings.LoadAsync(SettingsPath);
        Assert.Equal(connectionString, settings.ConnectionString);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"ConnectionStrings\":null}")]
    [InlineData("{\"ConnectionStrings\":[]}")]
    [InlineData("{\"ConnectionStrings\":{}}")]
    [InlineData("{\"ConnectionStrings\":{\"Pdm\":null}}")]
    [InlineData("{\"ConnectionStrings\":{\"Pdm\":false}}")]
    [InlineData("{\"ConnectionStrings\":{\"Pdm\":\"\"}}")]
    [InlineData("{\"ConnectionStrings\":{\"Pdm\":\"   \"}}")]
    public async Task MissingOrInvalidConnectionStringReturnsFriendlyError(string json)
    {
        await File.WriteAllTextAsync(SettingsPath, json);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AppSettings.LoadAsync(SettingsPath));
        Assert.Contains("ConnectionStrings.Pdm", error.Message);
    }

    [Fact]
    public async Task InvalidJsonPreservesParsingError()
    {
        await File.WriteAllTextAsync(SettingsPath, "{\"ConnectionStrings\":");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AppSettings.LoadAsync(SettingsPath));
        Assert.Contains("Не удалось прочитать настройки", error.Message);
        Assert.IsAssignableFrom<JsonException>(error.InnerException);
    }

    [Fact]
    public async Task MissingFilePreservesFileError()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => AppSettings.LoadAsync(SettingsPath));
        Assert.Contains("Не удалось прочитать настройки", error.Message);
        Assert.IsType<FileNotFoundException>(error.InnerException);
    }

    [Fact]
    public async Task CancellationIsNotConvertedToConfigurationError()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AppSettings.LoadAsync(SettingsPath, cancellation.Token));
    }

    [Fact]
    public async Task AppSettingsAreCopiedToApplicationOutput()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        var settings = await AppSettings.LoadAsync(path);
        Assert.False(string.IsNullOrWhiteSpace(settings.ConnectionString));
    }
}
