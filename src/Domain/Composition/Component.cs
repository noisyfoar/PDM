using Domain.Exceptions;
using Domain.Objects;

namespace Domain.Composition
{
    public sealed class Component
    {
        public PDMObject ChildObject { get; }
        public int Quantity { get; }

        public Component(PDMObject childObject, int quantity)
        {
            if (childObject is null)
                throw new PDMException("Дочерний объект не может отсутствовать.");

            if (quantity <= 0)
                throw new ArgumentException("Количество должно быть больше нуля.");

            ChildObject = childObject;
            Quantity = quantity;
        }
    }
}
