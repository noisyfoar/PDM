using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public sealed class PdmDbContextFactory : IDesignTimeDbContextFactory<PdmDbContext>
{
    public PdmDbContext CreateDbContext(string[] args)
    {
        var connectionString = "Host=localhost;Port=55432;Database=pdm;Username=admin;Password=admin";
        return new PdmDbContext(new DbContextOptionsBuilder<PdmDbContext>().UseNpgsql(connectionString).Options);
    }
}
