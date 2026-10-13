using Application.Interfaces.Persistence;
using Application.Interfaces.Services;

namespace Shell.ViewModels;

public sealed class MainViewModel
{
    public string Title => "PDM";
    internal IPdmObjectService ObjectService { get; }
    internal IImportCadFolderService ImportService { get; }
    internal ICompositionReader CompositionReader { get; }
    internal IAssemblyCalculationService CalculationService { get; }

    public MainViewModel(IPdmObjectService objectService, IImportCadFolderService importService,
        ICompositionReader compositionReader, IAssemblyCalculationService calculationService)
    {
        ObjectService = objectService ?? throw new ArgumentNullException(nameof(objectService));
        ImportService = importService ?? throw new ArgumentNullException(nameof(importService));
        CompositionReader = compositionReader ?? throw new ArgumentNullException(nameof(compositionReader));
        CalculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
    }
}
