using Domain.Exceptions;
using System.Text.RegularExpressions;

namespace Domain.Objects
{
    public class Designation
    {
        private static readonly Regex Pattern = new(@"\A[А-ЯЁ]{4}\.[0-9]{6}\.[0-9]{3}\z", RegexOptions.Compiled); // Пример формата: "АБВГ.123456.789"
        public string Value { get; private set; }

        private Designation(string value)
        {
            Value = value;
        }
        public static Designation Create(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Pattern.IsMatch(value)) // Проверка на null, пустую строку и соответствие формату
            {
                throw new PDMException("Неверный формат обозначения.");
            }
            return new Designation(value);
        }
        public override string ToString() => Value;
    }
}
