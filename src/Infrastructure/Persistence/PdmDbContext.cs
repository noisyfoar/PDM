using Domain.Components;
using Domain.Objects;
using Domain.Versions;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public sealed class PdmDbContext : DbContext
{
    public DbSet<PDMObject> Objects => Set<PDMObject>();
    public DbSet<ObjectVersion> Versions => Set<ObjectVersion>();
    public DbSet<Component> Components => Set<Component>();

    public PdmDbContext(DbContextOptions<PdmDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var objects = modelBuilder.Entity<PDMObject>();
        objects.ToTable("pdm_object");
        objects.HasKey(x => x.Id);
        objects.Property(x => x.ObjectType).HasConversion<string>().IsRequired();
        objects.Property(x => x.Designation)
            .HasConversion(x => x == null ? null : x.Value, x => x == null ? null : Designation.Create(x))
            .HasMaxLength(16);
        objects.HasIndex(x => x.Designation).IsUnique();
        objects.HasMany(x => x.Versions).WithOne(x => x.Owner).HasForeignKey("ObjectId").IsRequired();

        var versions = modelBuilder.Entity<ObjectVersion>();
        versions.ToTable("object_version");
        versions.HasKey(x => x.Id);
        versions.Property(x => x.VersionType).HasConversion<string>().IsRequired();
        versions.Property(x => x.Name).IsRequired().HasMaxLength(500);
        versions.Property(x => x.MassKg).HasPrecision(18, 6);
        versions.Property(x => x.CreatedAt).IsRequired();
        versions.HasIndex("ObjectId", nameof(ObjectVersion.VersionNumber)).IsUnique();
        versions.HasMany(x => x.Components).WithOne().HasForeignKey("ParentVersionId").IsRequired();

        var components = modelBuilder.Entity<Component>();
        components.ToTable("bom_link");
        components.Property<Guid>("Id");
        components.HasKey("Id");
        components.Property(x => x.Quantity).IsRequired();
        components.HasOne(x => x.ChildObject).WithMany().HasForeignKey("ChildObjectId").IsRequired().OnDelete(DeleteBehavior.Restrict);
        components.HasIndex("ParentVersionId", "ChildObjectId").IsUnique();
    }
}
