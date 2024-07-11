using CK.Setup;
using CK.Core;
using System.Collections.Generic;
using System;

namespace CK.Sqlite.Setup
{
    /// <summary>
    /// Driver for <see cref="SqliteDatabaseItem"/> item.
    /// </summary>
    public class SqliteDatabaseItemDriver : SetupItemDriver
    {
        readonly SqliteDatabaseConnectionItemDriver _connection;
        readonly ISetupSessionMemory _sessionMemory;
        readonly Dictionary<object, object> _sharedState;

        /// <summary>
        /// Initializes a new <see cref="SqliteDatabaseItemDriver"/>.
        /// </summary>
        /// <param name="info">Driver build information (required by base SetupItemDriver).</param>
        /// <param name="sessionMemory">Session memory service.</param>
        public SqliteDatabaseItemDriver( BuildInfo info, ISetupSessionMemory sessionMemory )
            : base( info )
        {
            _sessionMemory = sessionMemory;
            _connection = Drivers.Find<SqliteDatabaseConnectionItemDriver>( Item.ConnectionItem );
            _sharedState = new Dictionary<object, object>();
        }

        /// <summary>
        /// Masked Item to formally be associated to a <see cref="SqlDatabaseItem"/> item.
        /// </summary>
        public new SqliteDatabaseItem Item => (SqliteDatabaseItem)base.Item;

        /// <summary>
        /// Gets the Sql manager for this database.
        /// </summary>
        public ISqliteManagerBase SqliteManager => _connection.SqliteManager;

        /// <summary>
        /// Installs a script.
        /// </summary>
        /// <param name="monitor">Monitor to use.</param>
        /// <param name="script">The script to install.</param>
        /// <returns>True on succes, false on error.</returns>
        public bool InstallScript( IActivityMonitor monitor, ISetupScript script )
        {
            string body = script.GetScript();
            var tagHandler = new SimpleScriptTagHandler( body );
            if( !tagHandler.Expand( monitor, true ) ) return false;
            int idx = 0;
            foreach( var one in tagHandler.SplitScript() )
            {
                string key = script.Name.GetScriptKey( one.Label ?? "AutoLabel" + idx );
                if( !DoRun( monitor, one.Body, key ) ) return false;
                ++idx;
            }
            return true;
        }

        bool DoRun( IActivityMonitor monitor, string script, string key = null )
        {
            if( key != null && _sessionMemory.IsItemRegistered( key ) )
            {
                monitor.Trace( $"Script '{key}' has already been executed." );
                return true;
            }
            using( monitor.OpenTrace( $"Executing '{key ?? "<no key>"}'." ) )
            {
                if( SqliteManager.ExecuteOneScript( script, monitor ) )
                {
                    if( key != null ) _sessionMemory.RegisterItem( key );
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets a shared state for drivers: drivers from the same database can
        /// easily share any service scoped to their database: <see cref="RegisterService"/>
        /// and 
        /// </summary>
        public IDictionary<object, object> SharedState => _sharedState;

        /// <summary>
        /// Registers a unique service of a given type in the <see cref="SharedState"/>.
        /// The type <typeparamref name="T"/> must not already exist otherwise an exception is thrown.
        /// </summary>
        /// <typeparam name="T">The type of the service to register.</typeparam>
        /// <param name="service">The service instance.</param>
        public void RegisterService<T>( T service ) => _sharedState.Add( typeof( T ), service );

        /// <summary>
        /// Gets a previously registered service from the <see cref="SharedState"/>. 
        /// </summary>
        /// <typeparam name="T">The type of the service to retrieve.</typeparam>
        /// <param name="mustExist">True to throw an exception if the service is not registered.</param>
        /// <returns>The instance or null if it not registered and <paramref name="mustExist"/> is false.</returns>
        public T GetService<T>( bool mustExist = false ) => mustExist ? (T)_sharedState[typeof( T )] : (T)_sharedState.GetValueOrDefault( typeof( T ), null );
    }
}
