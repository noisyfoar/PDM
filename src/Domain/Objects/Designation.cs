using Domain.Exceptions;
using System.Text.RegularExpressions;

namespace Domain.Objects
{
    public class Designation
    {
        private static readonly Regex Pattern = new(@"^[А-ЯЁ]{4}\.\d{6}\.\d{3}$");
        public string Value { get; private set; }

        private Designation(string value)
        {
            Value = value;
        }
        public static Designation Create(string value)
        {
            if (!Pattern.IsMatch(value))
            {
                throw new PDMException("Invalid designation format.");
            }
            return new Designation(value);
        }
        public override string ToString() => Value;
    }
}
