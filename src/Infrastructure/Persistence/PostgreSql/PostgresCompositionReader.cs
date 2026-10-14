using Application.Interfaces.Persistence;
using Application.Records.Composition;
using Domain.Objects;
using Domain.Versions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Infrastructure.Persistence.PostgreSql;

public sealed class PostgresCompositionReader(DbContextOptions<PdmDbContext> options) : ICompositionReader
{
    private readonly DbContextOptions<PdmDbContext> _options = options ?? throw new ArgumentNullException(nameof(options));

    public async Task<IReadOnlyList<CompositionEntry>> GetAsync(Guid rootObjectId, CancellationToken ct = default)
    {
        await using var context = new PdmDbContext(_options);
        var rows = await context.Database.SqlQueryRaw<CompositionRow>(CompositionSql,
            new NpgsqlParameter("rootId", rootObjectId)).ToListAsync(ct);
        return rows.Select(row => new CompositionEntry(row.ObjectId, row.ParentObjectId, row.CurrentVersionId,
            Enum.Parse<ObjectType>(row.ObjectType), row.Designation, row.Name,
            row.VersionType is null ? null : Enum.Parse<ObjectVersionType>(row.VersionType),
            row.Material, row.MassKg, row.Quantity, row.TotalQuantity, row.Depth,
            Array.AsReadOnly(row.Path), row.IsCycle, row.CurrentVersionNumber)).ToList().AsReadOnly();
    }

    // Единственный SQL-запрос. UNION ALL сохраняет повторные вхождения по разным путям.
    // NULL в TotalQuantity обозначает переполнение decimal и передаётся в диагностику.
    private const string CompositionSql = """
        WITH RECURSIVE current_versions AS (
            SELECT DISTINCT ON ("ObjectId") *
            FROM object_version
            WHERE "VersionType" <> 'Annulled'
            ORDER BY "ObjectId", "VersionNumber" DESC
        ), tree AS (
            SELECT o."Id" AS "ObjectId", NULL::uuid AS "ParentObjectId",
                   v."Id" AS "CurrentVersionId", 1 AS "Quantity",
                   1::numeric AS "TotalQuantity", 0 AS "Depth",
                   ARRAY[o."Id"] AS "Path", false AS "IsCycle"
            FROM pdm_object o
            LEFT JOIN current_versions v ON v."ObjectId" = o."Id"
            WHERE o."Id" = @rootId
            UNION ALL
            SELECT child."Id", parent."ObjectId", v."Id", link."Quantity",
                   CASE WHEN parent."TotalQuantity" * link."Quantity" <= 79228162514264337593543950335
                        THEN parent."TotalQuantity" * link."Quantity" ELSE NULL::numeric END,
                   parent."Depth" + 1, parent."Path" || child."Id",
                   child."Id" = ANY(parent."Path")
            FROM tree parent
            JOIN bom_link link ON link."ParentVersionId" = parent."CurrentVersionId"
            JOIN pdm_object child ON child."Id" = link."ChildObjectId"
            LEFT JOIN current_versions v ON v."ObjectId" = child."Id"
            WHERE NOT parent."IsCycle"
        )
        SELECT tree.*, o."ObjectType", o."Designation",
               COALESCE(v."Name", latest."Name", o."Id"::text) AS "Name",
               v."VersionType", v."VersionNumber" AS "CurrentVersionNumber", v."Material", v."MassKg"
        FROM tree
        JOIN pdm_object o ON o."Id" = tree."ObjectId"
        LEFT JOIN current_versions v ON v."Id" = tree."CurrentVersionId"
        LEFT JOIN LATERAL (
            SELECT "Name" FROM object_version
            WHERE "ObjectId" = o."Id" ORDER BY "VersionNumber" DESC LIMIT 1
        ) latest ON tree."CurrentVersionId" IS NULL
        ORDER BY tree."Path"
        """;

    private sealed class CompositionRow
    {
        public Guid ObjectId { get; set; }
        public Guid? ParentObjectId { get; set; }
        public Guid? CurrentVersionId { get; set; }
        public string ObjectType { get; set; } = "";
        public string? Designation { get; set; }
        public string Name { get; set; } = "";
        public string? VersionType { get; set; }
        public int? CurrentVersionNumber { get; set; }
        public string? Material { get; set; }
        public decimal? MassKg { get; set; }
        public int Quantity { get; set; }
        public decimal? TotalQuantity { get; set; }
        public int Depth { get; set; }
        public Guid[] Path { get; set; } = [];
        public bool IsCycle { get; set; }
    }
}
