using Domain.Exceptions;

namespace Domain.Versions
{
    public class ObjectVersion
    {
        public Guid Id { get; set; }
        public Guid ObjectId { get; set; }
        public ObjectVersionType VersionType { get; private set; }
        public int VersionNumber { get; set; }
        public string? Material { get; private set; }
        private decimal? _massKg;
        public decimal? MassKg
        {
            get
            {
                return _massKg;
            }
            private set
            {
                if(value < 0)
                {
                    throw new PDMException("MassKg cannot be negative.");
                }
                _massKg = value;
            }
        }
        private readonly List<Composition.Component> _components = [];
        public IReadOnlyList<Composition.Component> Components => _components.AsReadOnly();

        public DateTime CreatedAt { get;}

        public ObjectVersion(Guid objectId, ObjectVersionType versionType, int versionNumber, string? material, decimal? massKg)
        {
            if (objectId == Guid.Empty) throw new PDMException("ObjectId cannot be empty.");
            if (versionNumber < 1) throw new PDMException("VersionNumber must be greater than zero.");
            Id = Guid.NewGuid();
            ObjectId = objectId;
            VersionType = versionType;
            VersionNumber = versionNumber;
            Material = material;
            MassKg = massKg;
            CreatedAt = DateTime.UtcNow;
        }
        public void Approve()
        {
            if (VersionType != ObjectVersionType.InWork)
            {
                throw new PDMException("Only InWork versions can be approved.");
            }
            VersionType = ObjectVersionType.Approved;
        }
        public void Disapprove()
        {
            if (VersionType == ObjectVersionType.Disapproved)
            {
                throw new PDMException("Disapproved version cannot be disapproved again.");
            }
            VersionType = ObjectVersionType.Disapproved;
        }

    }
}
