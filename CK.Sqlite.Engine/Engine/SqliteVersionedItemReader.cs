using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CK.Core;
using CK.Setup;
using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace CK.Sqlite.Setup;

/// <summary>
/// Implements <see cref="IVersionedItemReader"/>.
/// </summary>
public class SqliteVersionedItemReader : IVersionedItemReader
{
    /// <summary>
    /// Gets the current version of this store.
    /// </summary>
    public static int CurrentVersion { get; } = 1;

    SHA1Value? _runSignature;
    bool _initialized;

    public SqliteVersionedItemReader( ISqliteManager manager )
    {
        Throw.CheckNotNullArgument( manager );
        Manager = manager;
    }

    internal readonly ISqliteManager Manager;

    public static void AutoInitialize( ISqliteManager m )
    {
        using( m.Monitor.OpenTrace( "Installing SqlVersionedItemRepository store." ) )
        {
            m.ExecuteNonQuery( CreateVersionTableScript );
        }
    }

    public SHA1Value GetSignature( IActivityMonitor monitor )
    {
        if( !_runSignature.HasValue )
        {
            _runSignature = SHA1Value.Zero;
            if( Manager.ExecuteScalar( "SELECT 1 FROM sqlite_master WHERE type='table' AND name='CKCore_tItemVersionStore';" ) == null )
            {
                var s = (string)Manager.ExecuteScalar( "SELECT ItemVersion FROM CKCore_tItemVersionStore WHERE FullName='RunSignature';" );
                if( s != null )
                {
                    _ = SHA1Value.TryParse( s, out var v );
                    _runSignature = v;
                }
            }
            monitor.Trace( $"Current Sqlite database RunSignature: '{_runSignature.Value}'." );
        }
        return _runSignature.Value;
    }

    public OriginalReadInfo GetOriginalVersions( IActivityMonitor monitor )
    {
        var items = new List<VersionedTypedName>();
        var features = new List<VFeature>();
        if( !_initialized )
        {
            AutoInitialize( Manager );
            _initialized = true;
        }
        using( var c = new SqliteCommand( "select FullName, ItemType, ItemVersion from CKCore_tItemVersionStore where FullName <> 'CK.SqlVersionedItemRepository' and FullName <> 'RunSignature'" ) { Connection = Manager.Connection } )
        using( var r = c.ExecuteReader() )
        {
            while( r.Read() )
            {
                string fullName = r.GetString( 0 );
                string itemType = r.GetString( 1 );
                if( itemType == "VFeature" ) features.Add( new VFeature( fullName, CSemVer.SVersion.Parse( r.GetString( 2 ) ) ) );
                else items.Add( new VersionedTypedName( fullName, itemType, Version.Parse( r.GetString( 2 ) ) ) );
            }
        }
        monitor.Trace( $"Existing VFeatures: {features.Select( f => f.ToString() ).Concatenate()}" );
        return new OriginalReadInfo( items, features );
    }


    public VersionedName? OnVersionNotFound( IVersionedItem item, Func<string, VersionedTypedName> originalVersions ) => null;

    public VersionedName? OnPreviousVersionNotFound( IVersionedItem item, VersionedName prevVersion, Func<string, VersionedTypedName> originalVersions ) => null;

    internal static string CreateTemporaryTableScript = @"
pragma temp_store = MEMORY;
create temporary table if not exists TMP_T
(
	F text not null PRIMARY KEY,
	T text not null,
	V text not null
);
";

    internal static string MergeTemporaryTableScript = @"
insert or replace into CKCore_tItemVersionStore( FullName, ItemType, ItemVersion ) select F, T, V from TMP_T;";

    internal static string CreateVersionTableScript = @"
create table if not exists CKCore_tItemVersionStore
(
	FullName text not null PRIMARY KEY,
	ItemType text not null,
	ItemVersion text not null
);
insert or replace into CKCore_tItemVersionStore( FullName, ItemType, ItemVersion ) values( 'CK.SqlVersionedItemRepository', '', '0' );
";

}
