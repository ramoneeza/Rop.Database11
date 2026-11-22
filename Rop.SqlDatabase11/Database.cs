using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Rop.Database11.Logging;
using Rop.Database11.Tracking;
using Rop.Database11.TypeHandlers;

namespace Rop.Database11
{
    /// <summary>
    /// Implement a database class from abstract database for sql server
    /// </summary>
    public partial class Database:AbsDatabase,IDisposable
    {
        private readonly Database _parentdatabase;
        private readonly TableDependencyCycleTaskQueue _cycleTasks;
        private readonly ConcurrentDictionary<string,Database> _foreignDatabaseInstances;
        private readonly ConcurrentDictionary<string,Database> _foreignDatabasesPerServer;
        private readonly ConcurrentDictionary<RuntimeTypeHandle,Database> _foreignDatabasesPerType;
        private readonly ConcurrentDictionary<KeyDescription, Database> _foreignDatabasesPerKey;
        private readonly ILogger _logger;
        private bool _disposed;
        /// <summary>
        /// Logger used by the library (change tracking, cycle tasks). If none is
        /// provided, a fallback that writes to Debug.Print is used.
        /// </summary>
        public ILogger Logger => _logger;
        /// <summary>
        /// Initializes a new SQL Server database instance.
        /// </summary>
        /// <param name="strconn">Connection string to the database.</param>
        /// <param name="parentdatabase">Parent database instance; when null this instance becomes the root.</param>
        /// <param name="logger">Logger used by the library; defaults to a debug fallback logger.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="parentdatabase"/> is not a parent (root) database.</exception>
        public Database(string strconn,Database? parentdatabase=null, ILogger? logger = null) : base(strconn)
        {
            _parentdatabase = parentdatabase??this;
            _logger = logger ?? DebugLogger.Instance;
            if (parentdatabase == null)
            {
                _cycleTasks = new TableDependencyCycleTaskQueue(_logger);
                _foreignDatabasesPerType = new ConcurrentDictionary<RuntimeTypeHandle, Database>();
                _foreignDatabasesPerKey = new ConcurrentDictionary<KeyDescription, Database>();
                _foreignDatabaseInstances = new ConcurrentDictionary<string, Database>(StringComparer.OrdinalIgnoreCase);
                _foreignDatabasesPerServer = new ConcurrentDictionary<string, Database>(StringComparer.OrdinalIgnoreCase);
                _foreignDatabaseInstances[this.MainDatabaseName] = this;
                NewTypeHandlers.Ensure();
            }
            else
            {
                _cycleTasks = parentdatabase._cycleTasks;
                _foreignDatabaseInstances = parentdatabase._foreignDatabaseInstances;
                _foreignDatabasesPerType = parentdatabase._foreignDatabasesPerType;
                _foreignDatabasesPerKey = parentdatabase._foreignDatabasesPerKey;
                _foreignDatabasesPerServer = parentdatabase._foreignDatabasesPerServer;
                _foreignDatabaseInstances.TryAdd(this.MainDatabaseName,this);
            }
            if (!_parentdatabase.IsParentDataBase) throw new ArgumentException("ParentDatabase is no parent");
        }
        /// <summary>
        /// Indicates whether this instance is the root (parent) database.
        /// </summary>
        public bool IsParentDataBase => _parentdatabase == this;
        /// <summary>
        /// Creates a new SQL Server connection using this database connection string.
        /// </summary>
        /// <returns>A new <see cref="SqlConnection"/> instance.</returns>
        public override DbConnection FactoryConnection() => new SqlConnection(Strconn);
        /// <summary>
        /// Determines whether the table described by the key description resides in a foreign database.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <returns>True when the table belongs to a different database.</returns>
        public bool IsForeingTable(KeyDescription td)
        {
            return td.IsForeignTable && !td.ForeignDatabaseName.Equals(this.MainDatabaseName, StringComparison.OrdinalIgnoreCase);
        }
        /// <summary>
        /// Determines whether the table of the specified entity type resides in a foreign database.
        /// </summary>
        /// <typeparam name="T">Entity type whose table will be checked.</typeparam>
        /// <returns>True when the table belongs to a different database.</returns>
        /// <exception cref="Exception">Thrown when no table description is found for the type.</exception>
        public bool IsForeingTable<T>() where T:class
        {
            var td = GetKeyDescription<T>() ?? throw new Exception($"Table for {typeof(T).Name} not found");
            return IsForeingTable(td);
        }
        /// <summary>
        /// Returns the cached database instance for an external database, creating and caching it on first use.
        /// </summary>
        /// <param name="databasename">Name of the external database.</param>
        /// <returns>The database instance for the external database.</returns>
        public Database FactoryExternalDatabase(string databasename)
        {
            if (_foreignDatabaseInstances.TryGetValue(databasename, out var db)) return db;
            // SD-1: Replace corrupted the connection string if the DB name
            // appeared in server/user/password. The builder only touches InitialCatalog.
            var builder = new SqlConnectionStringBuilder(Strconn) { InitialCatalog = databasename };
            db=new Database(builder.ConnectionString,this._parentdatabase,_logger);
            return db;
        }
        /// <summary>
        /// Returns the database instance that owns the table described by the key description.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <returns>This instance for local tables, or the cached external database instance for foreign tables.</returns>
        public Database FactoryExternalDatabase(KeyDescription td)
        {
            if (_foreignDatabasesPerKey.TryGetValue(td, out var db)) return db;
            if (!IsForeingTable(td))
            {
                db = this;
                _foreignDatabasesPerKey[td] = db;
                return this;
            }
            db = FactoryExternalDatabase(td.ForeignDatabaseName);
            _foreignDatabasesPerKey[td] = db;
            return db;
        }
        /// <summary>
        /// Returns the database instance that owns the table of the specified entity type.
        /// </summary>
        /// <typeparam name="T">Entity type whose table owns the database.</typeparam>
        /// <returns>This instance for local tables, or the cached external database instance for foreign tables.</returns>
        /// <exception cref="ArgumentException">Thrown when no table description is found for the type.</exception>
        public Database FactoryExternalDatabase<T>() where T: class
        {
            // GE-3: programming error → exception with a concrete type.
            var td = GetKeyDescription<T>() ?? throw new ArgumentException($"Table for {typeof(T).Name} not found");
            return FactoryExternalDatabase(td);
        }
        /// <summary>
        /// Creates a SQL Server connection to the database that owns the table described by the key description.
        /// </summary>
        /// <param name="td">Key description of the table.</param>
        /// <returns>A new <see cref="SqlConnection"/> instance for the owning database.</returns>
        public DbConnection FactoryConnection(KeyDescription td)
        {
            var db=FactoryExternalDatabase(td);
            return new SqlConnection(db.Strconn);
        }
        /// <summary>
        /// Creates a SQL Server connection to the database that owns the table of the specified entity type.
        /// </summary>
        /// <typeparam name="T">Entity type whose table owns the database.</typeparam>
        /// <returns>A new <see cref="SqlConnection"/> instance for the owning database.</returns>
        /// <exception cref="ArgumentException">Thrown when no table description is found for the type.</exception>
        public DbConnection FactoryConnection<T>() where T:class
        {
            // GE-3: programming error → exception with a concrete type.
            var td = GetKeyDescription<T>()?? throw new ArgumentException($"Table for {typeof(T).Name} not found");
            return FactoryConnection(td);
        }
        /// <summary>
        /// Returns the cached database instance for an external database on the specified server.
        /// </summary>
        /// <param name="databasename">Name of the external database.</param>
        /// <param name="server">Name of the server hosting the database; when null or equal to the current server, the local server is used.</param>
        /// <returns>The cached database instance for the external database and server.</returns>
        public Database FactoryExternalDatabase(string databasename,string? server)
        {
            if (string.IsNullOrEmpty(server) || server.Equals(this.Server, StringComparison.OrdinalIgnoreCase))
            {
                return FactoryExternalDatabase(databasename);
            }
            // SD-18: cached by (database, server) like the other overloads, and as
            // a CHILD of the root (shares cycle tasks and is disposed with it):
            // previously every call created a new root with its own cycle tasks
            // (instance/task leak).
            var key = databasename + "\u0000" + server;
            if (_foreignDatabasesPerServer.TryGetValue(key, out var cached)) return cached;
            var builder = new SqlConnectionStringBuilder(Strconn)
            {
                InitialCatalog = databasename,
                DataSource = server
            };
            var db = new Database(builder.ConnectionString, this._parentdatabase, _logger);
            _foreignDatabasesPerServer[key] = db;
            return db;
        }
        /// <summary>
        /// Registers and returns a change tracking dependency for the specified table type.
        /// </summary>
        /// <param name="type">Entity type whose table changes will be tracked.</param>
        /// <param name="changesPriority">Priority of the change notifications.</param>
        /// <returns>The registered <see cref="SqlTableDependency"/> for the table.</returns>
        public SqlTableDependency GetTableDependency(Type type, ChangeTrackingPriority changesPriority)
        {
            var key = SqlTableDependency.CalcKey(this, type);
            return _cycleTasks.TryAdd(key, _ => new SqlTableDependency(this, type, changesPriority));
        }

        /// <summary>
        /// Disposes the change tracking cycle tasks. Only has effect on the root
        /// Database: external (child) instances share the parent's cycle service
        /// and are cached, so disposing them is a no-op.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            if (!IsParentDataBase) return;
            _disposed = true;
            _cycleTasks.Dispose();
        }
    }
}
