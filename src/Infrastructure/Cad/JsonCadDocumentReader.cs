using System.Text.Json;
using Contracts.CAD.Enums;
using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;

namespace Infrastructure.Cad;

public sealed class JsonCadDocumentReader : ICadDocumentReader
{
    public async Task<CadDocument> ReadAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = File.OpenRead(path);
        using var json = await JsonDocument.ParseAsync(stream, new JsonDocumentOptions(), ct);
        var root = json.RootElement;

        var type = root.GetProperty("type").GetString() switch
        {
            "Assembly" => CadDocumentType.Assembly,
            "Part" => CadDocumentType.Part,
            "StandardPart" => CadDocumentType.StandardPart,
            var value => throw new FormatException($"Неизвестный тип CAD-документа: {value}")
        };

        var properties = root.TryGetProperty("properties", out var p) ? p : default;
        decimal? mass = null;
        if (properties.TryGetProperty("mass", out var massElement))
            mass = massElement.GetDecimal();

        string? material = null;
        if (properties.TryGetProperty("material", out var materialElement))
            material = materialElement.GetString();

        var components = new List<CadComponent>();
        if (root.TryGetProperty("components", out var componentArray))
        {
            foreach (var component in componentArray.EnumerateArray())
                components.Add(new CadComponent(component.GetProperty("file").GetString()!, component.GetProperty("count").GetInt32()));
        }

        return new CadDocument
            (
                root.GetProperty("formatVersion").GetInt32(),
                root.GetProperty("fileName").GetString()!,
                type,
                root.TryGetProperty("designation", out var designation) && designation.ValueKind != JsonValueKind.Null ? designation.GetString() : null,
                root.GetProperty("name").GetString()!,
                material,
                mass,
                components
            );
    }
}
