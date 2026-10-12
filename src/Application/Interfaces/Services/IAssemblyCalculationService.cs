using Application.Records.Calculations;

namespace Application.Interfaces.Services;

public interface IAssemblyCalculationService
{
    Task<AssemblyCalculationResult> CalculateAsync(Guid assemblyId, CancellationToken ct = default);
}
