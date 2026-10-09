using Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Composition
{
    public class Component
    {
        private int _quantity;
        public int Quantity
        {
            get => _quantity;
            private set
            {
                if (value <= 0)
                {
                    throw new ArgumentException("Quantity must be greater than zero.");
                }
                _quantity = value;
            }
        }
        public Guid ChildObjectId { get; }
        public Component(Guid childObjectId, int quantity)
        {
            if(childObjectId == Guid.Empty) throw new PDMException("ChildObjectId cannot be empty.");
            ChildObjectId = childObjectId;
            Quantity = quantity;
        }
    }
}
