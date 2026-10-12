using Application.Interfaces.Persistence;
using Application.Interfaces.Services;
using Application.Records.Calculations;
using Application.Records.Composition;
using Domain.Objects;

namespace Application.Services;

public sealed class AssemblyCalculationService(ICompositionReader reader) : IAssemblyCalculationService
{
    private readonly ICompositionReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    public async Task<AssemblyCalculationResult> CalculateAsync(Guid assemblyId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        // Масса и спецификация рассчитываются по одному снимку, без запросов на каждый узел.
        var entries = await _reader.GetAsync(assemblyId, ct);
        var root = entries.SingleOrDefault(entry => entry.Depth == 0);
        var issues = new List<CalculationIssue>();
        if (root is null)
        {
            issues.Add(new(CalculationIssueKind.ObjectNotFound, assemblyId, "",
                "Выбранный объект не найден.", []));
            return new(assemblyId, null, [], issues.AsReadOnly());
        }
        if (root.ObjectType != ObjectType.Assembly)
        {
            issues.Add(Issue(root, CalculationIssueKind.NotAssembly, "Для расчёта необходимо выбрать сборку."));
            return new(assemblyId, null, [], issues.AsReadOnly());
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            if (entry.IsCycle)
                issues.Add(Issue(entry, CalculationIssueKind.Cycle,
                    $"Обнаружен цикл в составе: {string.Join(" → ", entry.Path)}."));
            if (entry.CurrentVersionId is null)
                issues.Add(Issue(entry, CalculationIssueKind.NoCurrentVersion,
                    $"У компонента «{entry.Name}» нет неаннулированной версии."));
            if (entry.Quantity <= 0 || entry.TotalQuantity <= 0)
                issues.Add(Issue(entry, CalculationIssueKind.InvalidQuantity,
                    $"У компонента «{entry.Name}» количество должно быть больше нуля."));
            else if (entry.TotalQuantity is null)
                issues.Add(Issue(entry, CalculationIssueKind.QuantityOverflow,
                    $"Переполнение количества по пути к компоненту «{entry.Name}»."));
        }
        // При неполном или циклическом составе нельзя выдавать частичную спецификацию за полную.
        if (issues.Count > 0) return new(assemblyId, null, [], issues.AsReadOnly());

        var specification = new List<SpecificationEntry>();
        decimal totalMass = 0;
        bool totalMassOverflow = false;
        foreach (var group in entries.Where(entry => entry.ObjectType != ObjectType.Assembly)
                     .GroupBy(entry => entry.ObjectId))
        {
            ct.ThrowIfCancellationRequested();
            var entry = group.First();
            decimal quantity = 0;
            try
            {
                foreach (var occurrence in group)
                    quantity = checked(quantity + occurrence.TotalQuantity!.Value);
            }
            catch (OverflowException)
            {
                issues.Add(Issue(entry, CalculationIssueKind.QuantityOverflow,
                    $"Переполнение суммарного количества компонента «{entry.Name}»."));
                return new(assemblyId, null, [], issues.AsReadOnly());
            }

            decimal? rowMass = null;
            if (entry.MassKg is null)
                issues.Add(Issue(entry, CalculationIssueKind.MissingMass,
                    $"У компонента «{entry.Name}» не указана масса."));
            else
            {
                try { rowMass = checked(entry.MassKg.Value * quantity); }
                catch (OverflowException)
                {
                    issues.Add(Issue(entry, CalculationIssueKind.MassOverflow,
                        $"Переполнение массы компонента «{entry.Name}»."));
                }
            }

            if (rowMass is not null && !totalMassOverflow)
            {
                try { totalMass = checked(totalMass + rowMass.Value); }
                catch (OverflowException)
                {
                    totalMassOverflow = true;
                    issues.Add(Issue(root, CalculationIssueKind.MassOverflow, "Переполнение общей массы сборки."));
                }
            }
            specification.Add(new(entry.ObjectId, entry.ObjectType, entry.Designation, entry.Name,
                entry.Material, quantity, entry.MassKg, rowMass));
        }

        var sorted = specification.OrderBy(entry => entry.Designation ?? entry.Name, StringComparer.Ordinal)
            .ThenBy(entry => entry.ObjectId).ToList().AsReadOnly();
        return new(assemblyId, issues.Count == 0 ? totalMass : null, sorted, issues.AsReadOnly());
    }

    private static CalculationIssue Issue(CompositionEntry entry, CalculationIssueKind kind, string message) =>
        new(kind, entry.ObjectId, entry.Name, message, entry.Path);
}
