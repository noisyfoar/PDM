using Contracts.CAD.Interfaces;
using Contracts.CAD.Records;
using System.Text.Json;

namespace Infrastructure.Cad;

public sealed class CadFolderDocumentReader : ICadFolderDocumentReader
{
    private readonly ICadDocumentReader _documentReader;

    public CadFolderDocumentReader(ICadDocumentReader documentReader)
    {
        _documentReader = documentReader ?? throw new ArgumentNullException(nameof(documentReader));
    }

    public async Task<CadFolderReadResult> ReadAsync(string folderPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        var documents = new List<CadDocument>();
        var errors = new List<CadReadError>();

        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                             .Where(IsCadFile)
                             .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        foreach (var path in files)
        {
            ct.ThrowIfCancellationRequested(); // проверка на отмену импорта CAD-файлов
            try
            {
                documents.Add(await _documentReader.ReadAsync(path, ct));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var message = ex is JsonException json
                    ? json.LineNumber is { } line && json.BytePositionInLine is { } position
                        ? $"Некорректный JSON-документ (строка {line + 1}, позиция {position + 1})."
                        : "Некорректный JSON-документ."
                    : ex.Message;
                errors.Add(new CadReadError(Path.GetFileName(path), message));
            }
        }
        ct.ThrowIfCancellationRequested();
        return new CadFolderReadResult(documents, errors);
    }

    private static bool IsCadFile(string path)
    {
        return path.EndsWith(".a3d", StringComparison.OrdinalIgnoreCase) 
            || path.EndsWith(".m3d", StringComparison.OrdinalIgnoreCase);
    }
}
