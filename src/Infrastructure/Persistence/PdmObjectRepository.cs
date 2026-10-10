using Application.Interfaces.Persistence;
using Domain.Objects;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

internal sealed class PdmObjectRepository : IPdmObjectRepository
{
    private readonly PdmDbContext _db;
    private readonly UnitOfWork _unitOfWork;

    public PdmObjectRepository(PdmDbContext db, UnitOfWork unitOfWork)
    {
        _db = db;
        _unitOfWork = unitOfWork;
    }

    public async Task<PDMObject?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        _unitOfWork.RequireTransaction();
        return await Query().SingleOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<PDMObject?> GetByDesignationAsync(Designation designation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(designation);
        _unitOfWork.RequireTransaction();
        var objects = await Query().ToListAsync(ct);
        return objects.SingleOrDefault(x => x.Designation?.Value == designation.Value);
    }

    public async Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default)
    {
        _unitOfWork.RequireTransaction();
        var objects = await Query()
            .Where(x => x.ObjectType == ObjectType.StandardPart)
            .ToListAsync(ct);
        return objects.SingleOrDefault(x => x.CurrentVersion?.Name == name);
    }

    public async Task<IReadOnlyList<PDMObject>> SearchAsync(
        string searchText,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);

        _unitOfWork.RequireTransaction();

        var objects = await Query().ToListAsync(ct);

        return objects
            .Where(obj =>
                obj.Designation?.Value.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase) == true ||
                obj.Name.Contains(
                    searchText,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void Add(PDMObject pdmObject)
    {
        ArgumentNullException.ThrowIfNull(pdmObject);
        _unitOfWork.RequireTransaction();
        _db.Objects.Add(pdmObject);
    }

    private IQueryable<PDMObject> Query() => _db.Objects
        .Include(x => x.Versions)
        .ThenInclude(x => x.Components)
        .ThenInclude(x => x.ChildObject)
        .ThenInclude(x => x.Versions)
        .AsSplitQuery();
}
