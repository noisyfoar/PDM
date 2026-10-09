using Domain.Exceptions;
using Domain.Versions;

namespace Domain.Tests;

public class ObjectVersionTests
{
    [Fact]
    public void Constructor_WithZeroVersionNumber_Throws()
    {
        Assert.Throws<PDMException>(() => new ObjectVersion(
            Guid.NewGuid(),
            ObjectVersionType.InWork,
            0,
            material: null,
            massKg: null));
    }

    [Fact]
    public void Approve_WhenVersionInWork_ChangesStateToApproved()
    {
        var version = CreateVersion(ObjectVersionType.InWork);

        version.Approve();

        Assert.Equal(ObjectVersionType.Approved, version.VersionType);
    }

    [Fact]
    public void Disapprove_WhenVersionApproved_ChangesStateToDisapproved()
    {
        var version = CreateVersion(ObjectVersionType.InWork);
        version.Approve();

        version.Disapprove();

        Assert.Equal(ObjectVersionType.Disapproved, version.VersionType);
    }

    private static ObjectVersion CreateVersion(ObjectVersionType versionType)
    {
        return new ObjectVersion(
            Guid.NewGuid(),
            versionType,
            1,
            material: null,
            massKg: null);
    }
}
