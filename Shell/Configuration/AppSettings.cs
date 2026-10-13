using System.IO;
using System.Text.Json;

namespace Shell.Configuration;

public sealed record AppSettings(string ConnectionString)
{
    public static async Task<AppSettings> LoadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ct.ThrowIfCancellationRequested();
        try
        {
            await using var stream = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("ConnectionStrings", out var connections)
                || connections.ValueKind != JsonValueKind.Object
                || !connections.TryGetProperty("Pdm", out var connection)
                || connection.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(connection.GetString()))
                throw new InvalidOperationException("В appsettings.json нужно указать непустую строку ConnectionStrings.Pdm.");
            return new AppSettings(connection.GetString()!);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException($"Не удалось прочитать настройки приложения: {error.Message}", error);
        }
    }
}
