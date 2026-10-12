namespace Domain.Versions
{
    public enum ObjectVersionType
    {
        /// <summary>
        /// Версия находится в работе
        /// </summary>
        InWork,
        /// <summary>
        /// Версия утверждена
        /// </summary>
        Approved,
        /// <summary>
        /// Версия аннулирована
        /// </summary>
        Annulled,
    }
}
