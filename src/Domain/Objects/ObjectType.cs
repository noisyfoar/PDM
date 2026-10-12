namespace Domain.Objects
{
    public enum ObjectType
    {
        /// <summary>
        /// Объект является сборкой
        /// </summary>
        Assembly,
        /// <summary>
        /// Объект является деталью
        /// </summary>
        Part,
        /// <summary>
        /// Объект является стандартной деталью
        /// </summary>
        StandardPart,
    }
}
