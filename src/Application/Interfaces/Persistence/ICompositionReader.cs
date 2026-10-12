using Application.Records.Composition;

namespace Application.Interfaces.Persistence;

public interface ICompositionReader
{
    // Каждая строка — отдельное вхождение, включая корень и замыкающий цикл узел.
    Task<IReadOnlyList<CompositionEntry>> GetAsync(Guid rootObjectId, CancellationToken ct = default);
}
