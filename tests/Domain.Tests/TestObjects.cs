using Domain.Composition;
using Domain.Objects;

namespace Domain.Tests;

internal static class TestObjects
{
    public static PDMObject Part(string name = "Вал", decimal? massKg = 2m)
        => new(ObjectType.Part, Designation.Create("АБВГ.301245.001"),
            name, material: "Сталь 40Х", massKg: massKg);

    public static PDMObject Assembly(IReadOnlyList<Component>? components = null)
        => new(ObjectType.Assembly, Designation.Create("АБВГ.301245.000"),
            "Сборка", components: components);
}
