using Application.Interfaces.Services;
using Application.Interfaces.Persistence;
using Application.Records.Composition;
using Application.Records.Calculations;
using Contracts.CAD.Records;
using Shell.Interfaces;
using Shell.ViewModels;

namespace Shell.Tests;

internal sealed class FakeCalculationService : IAssemblyCalculationService
{
    public Func<Guid, Task<AssemblyCalculationResult>>? Calculate { get; set; }
    public List<Guid> Calls { get; } = [];

    public Task<AssemblyCalculationResult> CalculateAsync(Guid assemblyId, CancellationToken ct = default)
    {
        Calls.Add(assemblyId);
        return Calculate?.Invoke(assemblyId) ?? Task.FromResult(new AssemblyCalculationResult(assemblyId, 0m, [], []));
    }
}

internal sealed class FakeImportService : IImportCadFolderService
{
    public ImportCadFolderReport Report { get; set; } = new([], [], []);
    public Func<string, Task<ImportCadFolderReport>>? Execute { get; set; }
    public int Calls { get; private set; }
    public string? LastFolder { get; private set; }

    public Task<ImportCadFolderReport> ExecuteAsync(string folderPath, CancellationToken ct = default)
    {
        Calls++;
        LastFolder = folderPath;
        return Execute?.Invoke(folderPath) ?? Task.FromResult(Report);
    }
}

internal sealed class FakeFolderPicker : IFolderPicker
{
    public string? Folder { get; set; }
    public Func<string?>? Select { get; set; }
    public int Calls { get; private set; }

    public string? SelectFolder()
    {
        Calls++;
        return Select is null ? Folder : Select();
    }
}

internal sealed class FakeCompositionReader : ICompositionReader
{
    public IReadOnlyList<CompositionEntry> Entries { get; set; } = [];
    public Func<Guid, Task<IReadOnlyList<CompositionEntry>>>? Read { get; set; }
    public List<Guid> Roots { get; } = [];

    public Task<IReadOnlyList<CompositionEntry>> GetAsync(Guid rootObjectId, CancellationToken ct = default)
    {
        Roots.Add(rootObjectId);
        return Read?.Invoke(rootObjectId) ?? Task.FromResult(Entries);
    }
}

internal sealed class FakeImportReportPresenter : IImportReportPresenter
{
    public List<ImportReportViewModel> Reports { get; } = [];
    public Action<ImportReportViewModel>? Present { get; set; }

    public void Show(ImportReportViewModel report)
    {
        Present?.Invoke(report);
        Reports.Add(report);
    }
}
