using System.Reflection;
using Rop.Database11.Repository;
using Rop.Database11.Tracking;

namespace Rop.Database11.PartialKeyRepository;


/// <summary>
/// Aggregate class for a block of items with the same first level key
/// </summary>
/// <typeparam name="K">Partial key type</typeparam>
/// <typeparam name="T">Entity type</typeparam>
public class PartialBlock<K,T> where K : notnull where T : class
{
    /// <summary>
    /// Gets the partial key shared by all items in the block.
    /// </summary>
    /// <returns>The partial key.</returns>
    public K Key { get; }
    /// <summary>
    /// Gets the items in the block.
    /// </summary>
    /// <returns>The items in the block.</returns>
    public IReadOnlyList<T> Block { get; }
    /// <summary>
    /// Initializes a partial block with a key and its items.
    /// </summary>
    /// <param name="key">Partial key shared by the items.</param>
    /// <param name="block">Items of the block.</param>
    public PartialBlock(K key, IEnumerable<T> block)
    {
        Key = key;
        Block = block.ToArray();
    }
}
/// <summary>
/// Base class for a simple SQL repository with partial key support and DTO.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <typeparam name="K">Partial key type</typeparam>
public abstract class AbsSimpleSqlPartialRepositoryK<T,D,K>:AbsSimpleSqlRepositoryK<PartialBlock<K,T>,PartialBlock<K,D>,K> where T:class where D:class where K:notnull
{
    /// <summary>
    /// Gets the partial key description for the repository.
    /// </summary>
    /// <returns>The partial key description.</returns>
    public override PartialKeyDescription KeyDescription =>(PartialKeyDescription)base.KeyDescription;
    /// <summary>
    /// Gets the partial key of the given DTO item.
    /// </summary>
    /// <param name="item">DTO instance.</param>
    /// <returns>Partial key of the item.</returns>
    public virtual K GetKey(D item) => (K)KeyDescription.GetKeyValue(item);
    /// <summary>
    /// Gets the key of the given partial block.
    /// </summary>
    /// <param name="item">Partial block instance.</param>
    /// <returns>Key of the block.</returns>
    public override K GetTKey(PartialBlock<K, T> item) => item.Key;
    /// <summary>
    /// Gets the key of the given DTO partial block.
    /// </summary>
    /// <param name="item">DTO partial block instance.</param>
    /// <returns>Key of the block.</returns>
    public override K GetDKey(PartialBlock<K, D> item) => item.Key;

    /// <summary>
    /// Maps a DTO item to the entity type.
    /// </summary>
    /// <param name="item">DTO instance.</param>
    /// <returns>Mapped entity.</returns>
    protected abstract T Map(D item);

    /// <summary>
    /// Maps a DTO block to an entity block.
    /// </summary>
    /// <param name="item">DTO partial block instance.</param>
    /// <returns>Mapped entity block.</returns>
    protected override PartialBlock<K, T> Map(PartialBlock<K, D> item)
    {
        var lst=item.Block.Select(Map);
        return new PartialBlock<K, T>(item.Key, lst);
    }

    /// <summary>
    /// Reloads all DTO items from the database.
    /// </summary>
    /// <returns>Result with the loaded DTOs or the error.</returns>
    protected abstract EnumerableResult<D> IntReloadAllPartial();

    /// <summary>
    /// Reloads all items from the database, grouping them into partial blocks.
    /// </summary>
    /// <returns>Result with the loaded partial blocks or the error.</returns>
    protected override EnumerableResult<PartialBlock<K, T>> IntReloadAll()
    {
        var rall= IntReloadAllPartial();
        if (rall.IsFailed) return rall.Error!;
        var gr=rall.Value.GroupBy(GetKey).Select(g =>Map(new PartialBlock<K, D>(g.Key, g.ToList())));
        return gr.ToList();
    }
    /// <summary>
    /// Reloads the DTO items with the given keys from the database.
    /// </summary>
    /// <param name="keys">Keys to reload.</param>
    /// <returns>Result with the loaded DTOs or the error.</returns>
    protected abstract EnumerableResult<D> IntReloadSomePartial(params IEnumerable<K> keys);
    /// <summary>
    /// Reloads the items with the given keys from the database, grouping them into partial blocks.
    /// </summary>
    /// <param name="keys">Keys to reload.</param>
    /// <returns>Result with the loaded partial blocks or the error.</returns>
    protected override EnumerableResult<PartialBlock<K, T>> IntReloadSome(params IEnumerable<K> keys)
    {
        var rall= IntReloadSomePartial(keys);
        if (rall.IsFailed) return rall.Error!;
        var gr=rall.Value.GroupBy(GetKey).Select(g => Map(new PartialBlock<K, D>(g.Key, g.ToList())));
        return gr.ToList();
    }

    // Constructor
    /// <summary>
    /// Initializes the repository using the partial key description derived from the DTO type.
    /// </summary>
    /// <param name="database">Database instance.</param>
    protected AbsSimpleSqlPartialRepositoryK(Database database):base(database,DapperHelperExtend.GetPartialKeyDescription(typeof(D)))
    {
    }

    /// <summary>
    /// The entity type of this repository is <see cref="PartialBlock{K,T}"/>, whose key
    /// is exposed by the <see cref="PartialBlock{K,T}.Key"/> property (not by the DTO key property).
    /// </summary>
    /// <param name="keydescription">Key description of the DTO type.</param>
    /// <returns>The <c>Key</c> property of the partial block type.</returns>
    protected override PropertyInfo? FindKeyPropT(KeyDescription keydescription)
        => typeof(PartialBlock<K, T>).GetProperty(nameof(PartialBlock<K, T>.Key));
}

/// <summary>
/// Base class for a simple SQL repository with partial key support and key of type int and DTO.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <param name="database">Database instance</param>
public abstract class AbsSimpleSqlPartialIntRepository<T,D>(Database database)
    : AbsSimpleSqlPartialRepositoryK<T,D, int>(database) where T : class where D: class
{
}
/// <summary>
/// Base class for a simple SQL repository with partial key support and key of type string and DTO.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <param name="database">Database instance</param>
public abstract class AbsSimpleSqlPartialRepository<T,D>(Database database)
    : AbsSimpleSqlPartialRepositoryK<T,D,string>(database) where T : class where D:class
{
}
/// <summary>
/// Base class for a simple SQL repository with partial key support without specific DTO and key of type string.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
/// <param name="database">Database instance</param>
public abstract class AbsSimpleSqlPartialRepository<T>(Database database)
    : AbsSimpleSqlPartialRepository<T, T>(database) where T : class
{
    /// <summary>
    /// Maps an entity to itself (identity mapping).
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>The same entity.</returns>
    protected override T Map(T item) => item;
}
/// <summary>
/// Base class for a simple SQL repository with partial key support without specific DTO and key of type int.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
/// <param name="database">Database instance</param>
public abstract class AbsSimpleSqlPartialIntRepository<T>(Database database)
    : AbsSimpleSqlPartialIntRepository<T, T>(database) where T : class
{
    /// <summary>
    /// Maps an entity to itself (identity mapping).
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>The same entity.</returns>
    protected override T Map(T item) => item;
}
/// <summary>
/// Base class for a SQL repository with partial key support and DTO and Change Tracking.
/// </summary>
/// <typeparam name="T">Entity type (business model)</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <typeparam name="K">Key type</typeparam>
public abstract class AbsSqlPartialRepositoryK<T, D, K> : AbsSimpleSqlPartialRepositoryK<T, D, K>, ISqlRepository<PartialBlock<K, T>, K> where T : class where D : class where K : notnull
{
    /// <summary>
    /// Gets the table dependency used for change tracking.
    /// </summary>
    /// <returns>The table dependency.</returns>
    public SqlTableDependency TableDependency { get; private set; }
    /// <summary>
    /// Gets the change tracking priority.
    /// </summary>
    /// <returns>The change tracking priority.</returns>
    public ChangeTrackingPriority ChangesPriority { get; }
    /// <summary>
    /// Gets the current change tracking version of the table.
    /// </summary>
    /// <returns>The tracking version.</returns>
    public TrackingVersion Version => TableDependency.TableVersion;

    /// <summary>
    /// Combines the two partial keys reported by change tracking into a single key.
    /// </summary>
    /// <param name="key1">First partial key.</param>
    /// <param name="key2">Second partial key.</param>
    /// <returns>Combined key.</returns>
    public abstract K CombineKeys(object? key1, object? key2);

    /// <summary>
    /// Creates the table dependency used for change tracking.
    /// </summary>
    /// <returns>The table dependency.</returns>
    protected virtual SqlTableDependency FactoryTableDependency()
    {
        return Database.GetTableDependency(typeof(D), ChangesPriority);
    }
    
    private void TableDependency_OnChanged(object? sender, DeltaChanges e)
    {
        if (ReloadOnAnyChange)
        {
            Reset();
        }
        else
        {
            var keys = e.GetPartialKeys().Select(t => CombineKeys(t.Item1, t.Item2)).ToArray();
            ResetIds(keys);
        }
    }
    
    /// <summary>
    /// Constructor for partial key repository with Change Tracking
    /// </summary>
    /// <param name="database">Database instance</param>
    /// <param name="changesPriority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
    protected AbsSqlPartialRepositoryK(Database database, ChangeTrackingPriority? changesPriority = null) : base(database)
    {
        ChangesPriority = changesPriority ?? ChangeTrackingPriority.Default;
        // ReSharper disable once VirtualMemberCallInConstructor
        TableDependency = FactoryTableDependency();
        TableDependency.OnChanged += TableDependency_OnChanged;
    }

    /// <summary>
    /// Reloads all DTO items from the database.
    /// </summary>
    /// <returns>Result with the loaded DTOs or the error.</returns>
    protected override EnumerableResult<D> IntReloadAllPartial()
    {
        return Database.GetAllNoKey<D>();
    }

    /// <summary>
    /// Reloads the DTO items with the given keys from the database.
    /// </summary>
    /// <param name="keys">Keys to reload.</param>
    /// <returns>Result with the loaded DTOs or the error.</returns>
    protected override EnumerableResult<D> IntReloadSomePartial(params IEnumerable<K> keys)
    {
        return Database.GetSome<K, D>(keys);
    }
}
/// <summary>
/// Base class for a SQL repository with partial key support and DTO and Change Tracking with key of type int.
/// </summary>
/// <typeparam name="T">Entity type (business model)</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <param name="database">Database instance</param>
/// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
public abstract class AbsSqlPartialDtoIntRepository<T, D>(Database database, ChangeTrackingPriority? priority = null)
    : AbsSqlPartialRepositoryK<T, D, int>(database, priority)
    where T : class
    where D : class
{
}
/// <summary>
/// Base class for a SQL repository with partial key support and DTO and Change Tracking with key of type string.
/// </summary>
/// <typeparam name="T">Entity type (business model)</typeparam>
/// <typeparam name="D">DTO type (database table representation)</typeparam>
/// <param name="database">Database instance</param>
/// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
public abstract class AbsSqlPartialDtoRepository<T, D>(Database database, ChangeTrackingPriority? priority = null)
    : AbsSqlPartialRepositoryK<T, D, string>(database, priority)
    where T : class
    where D : class
{
}
/// <summary>
/// Base class for a SQL repository with partial key support and Change Tracking,
/// without specific DTO and with key of type string.
/// </summary>
/// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
/// <param name="database">Database instance</param>
/// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
public abstract class AbsSqlPartialRepository<T>(Database database, ChangeTrackingPriority? priority = null)
    : AbsSqlPartialDtoRepository<T, T>(database, priority)
    where T : class
{
    /// <summary>
    /// Gets the string partial key of the given entity item.
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>Partial key of the item.</returns>
    public new virtual string GetKey(T item) => (string)KeyDescription.GetKeyValue(item);
    /// <summary>
    /// Maps an entity to itself (identity mapping).
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>The same entity.</returns>
    protected override T Map(T item) => item;
}
/// <summary>
/// Base class for a SQL repository with partial key support and Change Tracking,
/// without specific DTO and with key of type int.
/// </summary>
/// <typeparam name="T">Entity type, used both as entity and DTO.</typeparam>
/// <param name="database">Database instance</param>
/// <param name="priority">Change tracking priority. If null, uses Default (Medium - check every 16 seconds)</param>
public abstract class AbsSqlPartialIntRepository<T>(Database database, ChangeTrackingPriority? priority = null)
    : AbsSqlPartialDtoIntRepository<T, T>(database, priority)
    where T : class
{
    /// <summary>
    /// Gets the int partial key of the given entity item.
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>Partial key of the item.</returns>
    public new virtual int GetKey(T item) => (int)KeyDescription.GetKeyValue(item);
    /// <summary>
    /// Maps an entity to itself (identity mapping).
    /// </summary>
    /// <param name="item">Entity instance.</param>
    /// <returns>The same entity.</returns>
    protected override T Map(T item) => item;
}