using Contracts.CAD.Records;
using Contracts.CAD.Enums;
using Application.Interfaces.Services;
using Domain.Components;
using Domain.Exceptions;
using Domain.Objects;

namespace Application.Services;

public sealed class CadDomainMapper : ICadDomainMapper
{
    public IReadOnlyList<PDMObject> Map(IReadOnlyList<CadDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var objectsByFile = new Dictionary<string, PDMObject>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            ArgumentNullException.ThrowIfNull(document);
            if (!objectsByFile.TryAdd(document.FileName, CreateObject(document)))
                throw new PDMException($"Документ с именем «{document.FileName}» повторяется.");
        }

        foreach (var document in documents.Where(x => x.Type == CadDocumentType.Assembly))
        {
            var parent = objectsByFile[document.FileName];
            var components = new List<Component>();
            foreach (var link in document.Components)
            {
                if (!objectsByFile.TryGetValue(link.FileName, out var child))
                    throw new PDMException($"Файл компонента «{link.FileName}» не найден для «{document.FileName}».");

                components.Add(new Component(child, link.Quantity));
            }

            parent.CurrentVersion!.Update(parent.Name, null, null, components);
        }

        return objectsByFile.Values.ToList();
    }

    private static PDMObject CreateObject(CadDocument document)
    {
        var designation = document.Designation is null
            ? null
            : Designation.Create(document.Designation);

        return new PDMObject(
            ObjectTypeMapper.ToDomain(document.Type),
            designation,
            document.Name,
            document.Material,
            document.MassKg);
    }

}
