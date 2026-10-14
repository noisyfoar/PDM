using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Contracts.CAD.Interfaces;
using Infrastructure.Persistence.PostgreSql;
using Shell.Composition;
using Shell.Configuration;
using Shell.Interfaces;
using Shell.ViewModels;
using Xunit;

namespace Shell.Tests;

public sealed class AppServicesTests
{
    // Контейнер создаётся без InitializeAsync: никакие подключения и запросы к БД не выполняются.
    private static AppServices CreateProvider() => new(new AppSettings(
        "Host=localhost;Port=1;Database=pdm_jab_test;Username=test;Password=test"));

    [Fact]
    public async Task FactoryUsesSameDatabaseAndCreatesSeparateUnitsOfWork()
    {
        using var provider = CreateProvider();
        var database = provider.GetService<PostgresDatabase>();
        var factory = provider.GetService<IUnitOfWorkFactory>();
        Assert.Same(database, factory);
        Assert.Same(database, provider.GetService<PostgresDatabase>());
        Assert.Same(factory, provider.GetService<IUnitOfWorkFactory>());
        await using var first = factory.CreateUnitOfWork();
        await using var second = factory.CreateUnitOfWork();
        Assert.NotSame(first, second);
        Assert.NotSame(first.Objects, second.Objects);
    }

    [Fact]
    public void ResolvesViewModelAndAllApplicationDependenciesBeforeDatabaseInitialization()
    {
        using var provider = CreateProvider();
        Assert.Same(provider.ViewModel, provider.GetService<MainViewModel>());
        Assert.Same(provider.GetService<IPdmObjectService>(), provider.GetService<IPdmObjectService>());
        Assert.Same(provider.GetService<IImportCadFolderService>(), provider.GetService<IImportCadFolderService>());
        Assert.Same(provider.GetService<IAssemblyCalculationService>(), provider.GetService<IAssemblyCalculationService>());
        Assert.Same(provider.GetService<ICompositionReader>(), provider.GetService<ICompositionReader>());
        Assert.Same(provider.GetService<ICadDocumentReader>(), provider.GetService<ICadDocumentReader>());
        Assert.Same(provider.GetService<ICadFolderDocumentReader>(), provider.GetService<ICadFolderDocumentReader>());
        Assert.Same(provider.GetService<ICadDomainMapper>(), provider.GetService<ICadDomainMapper>());
        Assert.Same(provider.GetService<IFolderPicker>(), provider.GetService<IFolderPicker>());
        Assert.Same(provider.GetService<IImportReportPresenter>(), provider.GetService<IImportReportPresenter>());
        Assert.Same(provider.Settings, provider.GetService<AppSettings>());
    }

    [Fact]
    public void SeparateProvidersHaveSeparateDatabaseAndViewModelInstances()
    {
        using var first = CreateProvider();
        using var second = CreateProvider();
        Assert.NotSame(first.GetService<PostgresDatabase>(), second.GetService<PostgresDatabase>());
        Assert.NotSame(first.ViewModel, second.ViewModel);
        Assert.NotSame(first.GetService<IPdmObjectService>(), second.GetService<IPdmObjectService>());
    }

    [Fact]
    public async Task ContainerAlsoSupportsAsynchronousCleanupWithoutDatabaseConnection()
    {
        await using var provider = CreateProvider();
        _ = provider.ViewModel;
        Assert.Same(provider.GetService<PostgresDatabase>(), provider.GetService<IUnitOfWorkFactory>());
    }
}
