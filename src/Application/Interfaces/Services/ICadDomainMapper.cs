using Contracts.CAD.Records;
using Domain.Objects;

namespace Application.Interfaces.Services;

public interface ICadDomainMapper
{
    // Создаёт объект с атрибутами. Состав связывает сервис импорта.
    PDMObject Map(CadDocument document);
}
