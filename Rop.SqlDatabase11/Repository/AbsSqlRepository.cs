using Rop.Database11.Tracking;

namespace Rop.Database11.Repository
{
    /// <summary>
    /// Base class for SQL repositories with table dependency and versioning without specific DTO and key of type string.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="database">Database instance</param>
    /// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
    public abstract class AbsSqlRepository<T>(Database database, ChangeTrackingPriority? priority = null) 
        : AbsSqlDtoRepository<T, T>(database, priority)
        where T : class
    {
        /// <summary>
        /// Gets the string key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        public virtual string GetKey(T item) => base.GetTKey(item);
        /// <summary>
        /// Gets the key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        public override string GetTKey(T item) => GetKey(item);
        /// <summary>
        /// Gets the key of the given DTO item.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Key of the item.</returns>
        public override string GetDKey(T item) => GetKey(item);
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item) => item;
    }

    /// <summary>
    /// Base class for SQL repositories with table dependency and versioning without specific DTO and key of type int.
    /// </summary>
    /// <typeparam name="T">Entity type</typeparam>
    /// <param name="database">Database instance</param>
    /// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
    public abstract class AbsSqlIntRepository<T>(Database database, ChangeTrackingPriority? priority = null) 
        : AbsSqlDtoIntRepository<T, T>(database, priority)
        where T : class
    {
        /// <summary>
        /// Gets the int key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        public virtual int GetKey(T item) => base.GetTKey(item);
        /// <summary>
        /// Gets the key of the given entity item.
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>Key of the item.</returns>
        public override int GetTKey(T item) => GetKey(item);
        /// <summary>
        /// Gets the key of the given DTO item.
        /// </summary>
        /// <param name="item">DTO instance.</param>
        /// <returns>Key of the item.</returns>
        public override int GetDKey(T item) => GetKey(item);
        /// <summary>
        /// Maps an entity to itself (identity mapping).
        /// </summary>
        /// <param name="item">Entity instance.</param>
        /// <returns>The same entity.</returns>
        protected override T Map(T item) => item;
    }
}