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
        return await Query().SingleOrDefaultAsync(x => x.Designation == designation, ct);
    }

    public async Task<PDMObject?> GetStandardPartByNameAsync(string name, CancellationToken ct = default)
    {
        _unitOfWork.RequireTransaction();
        return await Query().Where(x => x.ObjectType == ObjectType.StandardPart)
            .SingleOrDefaultAsync(x => x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.Name).First() == name, ct);
    }

    public async Task<IReadOnlyList<PDMObject>> SearchAsync(string searchText, CancellationToken ct = default)
    {
        _unitOfWork.RequireTransaction();
        searchText ??= string.Empty;
        var objects = await Query().ToListAsync(ct);
        return objects.Where(x => (x.Designation?.Value.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
            x.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public async Task SaveAsync(PDMObject pdmObject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pdmObject);
        _unitOfWork.RequireTransaction();
        var tracked = await _db.Objects.FindAsync([pdmObject.Id], ct);
        if (tracked is null) _db.Objects.Add(pdmObject);
        else _db.Entry(tracked).CurrentValues.SetValues(pdmObject);
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable<PDMObject> Query() => _db.Objects
        .Include(x => x.Versions)
        .ThenInclude(x => x.Components)
        .ThenInclude(x => x.ChildObject)
        .AsSplitQuery();
}
