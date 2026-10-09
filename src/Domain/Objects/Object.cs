namespace Domain.Objects
{
    public class Object
    {
        public Guid Id { get; set; }
        public ObjectType ObjectType { get; set; }
        public Designation? Designation { get; set; }
        public required string Name { get; set; }
        public Guid CurrentVersionId { get; set; }
    }
}
