using Application.Interfaces.Persistence;
using Application.Records.Calculations;
using Application.Records.Composition;
using Application.Services;
using Domain.Objects;
using Domain.Versions;
using Xunit;

namespace Infrastructure.Tests;

public sealed class AssemblyCalculationServiceTests
{
    private readonly Guid _root = Guid.NewGuid();

    [Fact]
    public async Task NestedAndRepeatedComponentsAreAggregatedByObjectUsingOneSnapshot()
    {
        var sub = Guid.NewGuid();
        var part = Guid.NewGuid();
        var bolt = Guid.NewGuid();
        var reader = new Reader([
            Entry(_root, ObjectType.Assembly, [_root]),
            Entry(sub, ObjectType.Assembly, [_root, sub], quantity: 3, total: 3),
            Entry(part, ObjectType.Part, [_root, sub, part], 2m, 4, 12),
            Entry(part, ObjectType.Part, [_root, part], 2m, 2, 2),
            Entry(bolt, ObjectType.StandardPart, [_root, sub, bolt], 0.5m, 2, 6)]);

        var result = await new AssemblyCalculationService(reader).CalculateAsync(_root);

        Assert.True(result.IsSuccessful);
        Assert.Equal(31m, result.MassKg);
        Assert.Equal(1, reader.Calls);
        Assert.Equal(_root, reader.RootId);
        Assert.Equal(2, result.Specification.Count);
        Assert.DoesNotContain(result.Specification, row => row.ObjectType == ObjectType.Assembly);
        var partRow = Assert.Single(result.Specification, row => row.ObjectId == part);
        Assert.Equal(14m, partRow.Quantity);
        Assert.Equal(2m, partRow.UnitMassKg);
        Assert.Equal(28m, partRow.TotalMassKg);
        Assert.Equal("Сталь", partRow.Material);
        Assert.Equal(6m, Assert.Single(result.Specification, row => row.ObjectId == bolt).Quantity);
    }

    [Fact]
    public async Task EmptyAssemblyHasZeroMassAndEmptySpecification()
    {
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]));
        Assert.True(result.IsSuccessful);
        Assert.Equal(0m, result.MassKg);
        Assert.Empty(result.Specification);
    }

    [Fact]
    public async Task ZeroMassIsKnownMass()
    {
        var part = Guid.NewGuid();
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]),
            Entry(part, ObjectType.Part, [_root, part], 0m, 2, 2));
        Assert.True(result.IsSuccessful);
        Assert.Equal(0m, result.MassKg);
        Assert.Equal(0m, Assert.Single(result.Specification).TotalMassKg);
    }

    [Theory]
    [InlineData(ObjectType.Part)]
    [InlineData(ObjectType.StandardPart)]
    public async Task MissingMassKeepsFullSpecificationButNeverReturnsPartialTotal(ObjectType type)
    {
        var missing = Guid.NewGuid();
        var known = Guid.NewGuid();
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]),
            Entry(missing, type, [_root, missing]), Entry(known, ObjectType.Part, [_root, known], 2m));
        Assert.False(result.IsSuccessful);
        Assert.Null(result.MassKg);
        Assert.Equal(2, result.Specification.Count);
        Assert.Null(Assert.Single(result.Specification, row => row.ObjectId == missing).TotalMassKg);
        Assert.Equal(2m, Assert.Single(result.Specification, row => row.ObjectId == known).TotalMassKg);
        var issue = Assert.Single(result.Issues);
        Assert.Equal(CalculationIssueKind.MissingMass, issue.Kind);
        Assert.Equal(missing, issue.ObjectId);
        Assert.Equal(new[] { _root, missing }, issue.Path);
    }

    [Theory]
    [InlineData(CalculationIssueKind.Cycle)]
    [InlineData(CalculationIssueKind.NoCurrentVersion)]
    [InlineData(CalculationIssueKind.InvalidQuantity)]
    [InlineData(CalculationIssueKind.QuantityOverflow)]
    public async Task BrokenCompositionDoesNotProducePartialSpecification(CalculationIssueKind kind)
    {
        var id = Guid.NewGuid();
        var invalid = Entry(id, ObjectType.Part, [_root, id], 1m);
        invalid = kind switch
        {
            CalculationIssueKind.Cycle => invalid with { IsCycle = true },
            CalculationIssueKind.NoCurrentVersion => invalid with { CurrentVersionId = null },
            CalculationIssueKind.InvalidQuantity => invalid with { Quantity = 0 },
            _ => invalid with { TotalQuantity = null }
        };
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]), invalid);
        Assert.Null(result.MassKg);
        Assert.Empty(result.Specification);
        Assert.Equal(kind, Assert.Single(result.Issues).Kind);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task NonPositivePathQuantityIsRejected(int quantity)
    {
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]) with { TotalQuantity = quantity });
        Assert.Equal(CalculationIssueKind.InvalidQuantity, Assert.Single(result.Issues).Kind);
        Assert.Null(result.MassKg);
    }

    [Fact]
    public async Task AnnulledRootHasNoCalculation()
    {
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]) with { CurrentVersionId = null });
        Assert.Equal(CalculationIssueKind.NoCurrentVersion, Assert.Single(result.Issues).Kind);
        Assert.Empty(result.Specification);
    }

    [Fact]
    public async Task MissingObjectIsDiagnosed()
    {
        var result = await Calculate();
        Assert.Equal(_root, result.AssemblyId);
        Assert.Equal(CalculationIssueKind.ObjectNotFound, Assert.Single(result.Issues).Kind);
        Assert.Null(result.MassKg);
    }

    [Theory]
    [InlineData(ObjectType.Part)]
    [InlineData(ObjectType.StandardPart)]
    public async Task OnlyAssembliesCanBeCalculated(ObjectType type)
    {
        var result = await Calculate(Entry(_root, type, [_root], 2m));
        Assert.Equal(CalculationIssueKind.NotAssembly, Assert.Single(result.Issues).Kind);
        Assert.Null(result.MassKg);
    }

    [Fact]
    public async Task AggregateQuantityOverflowDoesNotWrap()
    {
        var part = Guid.NewGuid();
        var sub = Guid.NewGuid();
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]),
            Entry(part, ObjectType.Part, [_root, part], 1m, total: decimal.MaxValue),
            Entry(part, ObjectType.Part, [_root, sub, part], 1m));
        Assert.Null(result.MassKg);
        Assert.Empty(result.Specification);
        Assert.Equal(CalculationIssueKind.QuantityOverflow, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task RowMassOverflowKeepsQuantityButNoInventedMass()
    {
        var part = Guid.NewGuid();
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]),
            Entry(part, ObjectType.Part, [_root, part], decimal.MaxValue, 2, 2));
        Assert.Null(result.MassKg);
        Assert.Null(Assert.Single(result.Specification).TotalMassKg);
        Assert.Equal(2m, result.Specification[0].Quantity);
        Assert.Equal(CalculationIssueKind.MassOverflow, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task TotalMassOverflowKeepsIndividualRows()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var result = await Calculate(Entry(_root, ObjectType.Assembly, [_root]),
            Entry(first, ObjectType.Part, [_root, first], decimal.MaxValue),
            Entry(second, ObjectType.StandardPart, [_root, second], 1m));
        Assert.Null(result.MassKg);
        Assert.Equal(2, result.Specification.Count);
        Assert.All(result.Specification, row => Assert.NotNull(row.TotalMassKg));
        Assert.Equal(CalculationIssueKind.MassOverflow, Assert.Single(result.Issues).Kind);
    }

    [Fact]
    public async Task CancellationBeforeReadDoesNotAccessReader()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reader = new Reader([]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AssemblyCalculationService(reader).CalculateAsync(_root, cancellation.Token));
        Assert.Equal(0, reader.Calls);
    }

    [Fact]
    public async Task CancellationDuringReadIsRespectedEvenForEmptySnapshot()
    {
        using var cancellation = new CancellationTokenSource();
        var reader = new Reader([]) { OnRead = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new AssemblyCalculationService(reader).CalculateAsync(_root, cancellation.Token));
        Assert.Equal(cancellation.Token, reader.Token);
    }

    [Fact]
    public async Task ReaderFailureIsNotTurnedIntoSuccessfulEmptyAssembly()
    {
        var error = new InvalidOperationException("Ошибка чтения состава.");
        var reader = new Reader([]) { Error = error };
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AssemblyCalculationService(reader).CalculateAsync(_root)));
    }

    private Task<AssemblyCalculationResult> Calculate(params CompositionEntry[] entries) =>
        new AssemblyCalculationService(new Reader(entries)).CalculateAsync(_root);

    private static CompositionEntry Entry(Guid id, ObjectType type, Guid[] path, decimal? mass = null,
        int quantity = 1, decimal total = 1) =>
        new(id, path.Length == 1 ? null : path[^2], Guid.NewGuid(), type,
            type == ObjectType.StandardPart ? null : "АБВГ.123456.001", type.ToString(),
            ObjectVersionType.InWork, type == ObjectType.Part ? "Сталь" : null,
            mass, quantity, total, path.Length - 1, path, false, 1);

    private sealed class Reader(IReadOnlyList<CompositionEntry> entries) : ICompositionReader
    {
        public int Calls { get; private set; }
        public Guid RootId { get; private set; }
        public CancellationToken Token { get; private set; }
        public Action? OnRead { get; init; }
        public Exception? Error { get; init; }
        public Task<IReadOnlyList<CompositionEntry>> GetAsync(Guid rootObjectId, CancellationToken ct = default)
        {
            Calls++;
            RootId = rootObjectId;
            Token = ct;
            OnRead?.Invoke();
            return Error is null ? Task.FromResult(entries) : Task.FromException<IReadOnlyList<CompositionEntry>>(Error);
        }
    }
}
