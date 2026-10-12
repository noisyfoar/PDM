using Domain.Objects;

namespace Application.Interfaces.Persistence;

// ToDo: добавить репозиторий для тестов, который живет в оперативной памяти и не ходит в базу данных. 
public interface IPdmObjectRepository
{
    Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default);

    // Для повторного импорта: поиск существующей сборки или детали по обозначению.
    Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default);

    // Для повторного импорта: поиск существующего стандартного изделия по наименованию.
    Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default);
    // Поиск объектов по тексту в наименовании или обозначении.
    Task<IReadOnlyList<PDMObject>> SearchAsync(string searchText, CancellationToken ct = default);
    void Add(PDMObject pdmObject);
}
