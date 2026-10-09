using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Exceptions
{
    public class PDMException : Exception
    {
        public PDMException(string message) : base(message)
        {
        }
    }
}
