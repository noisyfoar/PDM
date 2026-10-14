using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Services;
using Contracts.CAD.Interfaces;
using Infrastructure.Cad;
using Infrastructure.Persistence.PostgreSql;
using Jab;
using Shell.Configuration;
using Shell.Interfaces;
using Shell.Services;
using Shell.ViewModels;

namespace Shell.Composition;

[ServiceProvider]
[Singleton(typeof(AppSettings), Instance = nameof(Settings))]
[Singleton(typeof(PostgresDatabase), Factory = nameof(CreateDatabase))]
[Singleton(typeof(IUnitOfWorkFactory), Instance = nameof(UnitOfWorkFactory))]
[Singleton(typeof(ICadDocumentReader), typeof(JsonCadDocumentReader))]
[Singleton(typeof(ICadFolderDocumentReader), typeof(CadFolderDocumentReader))]
[Singleton(typeof(ICadDomainMapper), typeof(CadDomainMapper))]
[Singleton(typeof(ICompositionReader), Factory = nameof(CreateCompositionReader))]
[Singleton(typeof(IPdmObjectService), typeof(PdmObjectService))]
[Singleton(typeof(IImportCadFolderService), typeof(ImportCadFolderService))]
[Singleton(typeof(IAssemblyCalculationService), typeof(AssemblyCalculationService))]
[Singleton(typeof(IFolderPicker), typeof(FolderPicker))]
[Singleton(typeof(IImportReportPresenter), typeof(ImportReportPresenter))]
[Singleton(typeof(MainViewModel))]
[Transient(typeof(MainWindow))]
public partial class AppServices
{
    public AppSettings Settings { get; }
    public MainViewModel ViewModel => GetService<MainViewModel>();

    // Ссылка на уже принадлежащую контейнеру базу: второй владелец IDisposable не создаётся.
    private IUnitOfWorkFactory UnitOfWorkFactory => GetService<PostgresDatabase>();

    public AppServices(AppSettings settings)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await GetService<PostgresDatabase>().InitializeAsync(ct);
        await ViewModel.InitializeAsync();
    }

    private PostgresDatabase CreateDatabase(AppSettings settings) => new(settings.ConnectionString);
    private ICompositionReader CreateCompositionReader(PostgresDatabase database) => database.CreateCompositionReader();
}
