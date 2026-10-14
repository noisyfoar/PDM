using Contracts.CAD.Records;

namespace Shell.ViewModels;

public sealed class ImportReportViewModel
{
    public string Title => "Отчёт импорта";
    public string FolderPath { get; }
    public ImportCadFolderReport Report { get; }
    public string? Notification { get; }
    public string Summary => $"Принято: {Report.Accepted.Count}. Отклонено: {Report.Rejected.Count}. Предупреждения: {Report.Warnings.Count}.";

    public ImportReportViewModel(string folderPath, ImportCadFolderReport report, string? notification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(report);
        FolderPath = folderPath;
        // Открытое окно сохраняет свой отчёт при последующих импортах.
        Report = new(Array.AsReadOnly(report.Accepted.ToArray()),
            Array.AsReadOnly(report.Rejected.ToArray()), Array.AsReadOnly(report.Warnings.ToArray()));
        Notification = notification;
    }
}
