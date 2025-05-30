using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using CK.Core;
using CK.Setup;
using System.Diagnostics;
using System.Text;

namespace CK.Sqlite.Setup;

/// <summary>
/// Implements <see cref="IVersionedItemWriter"/> on a Sqlite database.
/// </summary>
public class SqliteVersionedItemWriter : IVersionedItemWriter
{
    readonly ISqliteManagerBase _manager;
    bool _initialized;

    /// <summary>
    /// Initializes a new <see cref="SqliteVersionedItemWriter"/>.
    /// </summary>
    /// <param name="m">The sql manager to use.</param>
    public SqliteVersionedItemWriter( ISqliteManagerBase m )
    {
        Throw.CheckNotNullArgument( m );
        _manager = m;
    }

    /// <inheritdoc />
    public void SetVersions( IActivityMonitor monitor,
                             IVersionedItemReader reader,
                             IEnumerable<VersionedNameTracked> trackedItems,
                             bool deleteUnaccessedItems,
                             IReadOnlyCollection<VFeature> originalFeatures,
                             IReadOnlyCollection<VFeature> finalFeatures,
                             in SHA1Value runSignature )
    {
        bool rewriteToSameDatabase = reader is SqliteVersionedItemReader sqlReader && sqlReader.Manager == _manager;
        if( !rewriteToSameDatabase && !_initialized && _manager is ISqliteManager actualManager )
        {
            SqliteVersionedItemReader.AutoInitialize( actualManager );
            _initialized = true;
        }
        StringBuilder delete = null;
        StringBuilder update = null;

        void Delete( string fullName, bool hasBeenAccessed )
        {
            if( delete == null ) delete = new StringBuilder( "delete from CKCore.tItemVersionStore where FullName in (" );
            else delete.Append( ',' );
            delete.Append( "'" ).Append( SqliteHelper.SqliteEncodeStringContent( fullName ) ).Append( '\'' );
            if( !hasBeenAccessed )
            {
                monitor.Info( $"Item '{fullName}' has not been accessed: deleting its version information." );
            }
            else monitor.Info( $"Deleting '{fullName}' version information." );
        }

        void Update( string fullName, string typeName, string version )
        {
            if( update == null )
            {
                update = new StringBuilder( SqliteVersionedItemReader.CreateTemporaryTableScript );
                update.Append( "insert into TMP_T( F, T, V ) values " );
            }
            else update.Append( ',' );
            update.Append( "('" ).Append( SqliteHelper.SqliteEncodeStringContent( fullName ) )
                .Append( "','" ).Append( SqliteHelper.SqliteEncodeStringContent( typeName ) )
                .Append( "','" ).Append( version ).Append( "')" );
        }

        Debug.Assert( reader != null || !rewriteToSameDatabase, "reader == null ==> !rewriteToSameDatabase" );
        if( !rewriteToSameDatabase || runSignature != reader.GetSignature( monitor ) )
        {
            Update( "RunSignature", "RunSignature", runSignature.ToString() );
        }

        foreach( VersionedNameTracked t in trackedItems )
        {
            bool mustDelete = t.Deleted || (deleteUnaccessedItems && !t.Accessed);
            if( mustDelete )
            {
                Delete( t.FullName, t.Accessed );
            }
            else if( t.Original == null )
            {
                monitor.Trace( $"Item '{t.FullName}' is a new one." );
                Update( t.FullName, t.NewType, t.NewVersion.ToString() );
            }
            else if( t.NewVersion != null )
            {
                bool mustUpdate = false;
                if( t.NewVersion > t.Original.Version )
                {
                    monitor.Trace( $"Item '{t.FullName}': version is upgraded." );
                    mustUpdate = true;
                }
                else if( t.NewVersion < t.Original.Version )
                {
                    monitor.Warn( $"Item '{t.FullName}': version downgraded from {t.Original.Version} to {t.NewVersion}." );
                    mustUpdate = true;
                }
                if( t.NewType != t.Original.Type )
                {
                    monitor.Warn( $"Item '{t.FullName}': Type change from '{t.Original.Type}' to '{t.NewType}'." );
                    mustUpdate = true;
                }
                if( mustUpdate ) Update( t.FullName, t.NewType, t.NewVersion.ToString() );
            }
            else
            {
                if( !rewriteToSameDatabase )
                {
                    monitor.Trace( $"Item '{t.FullName}' has not been accessed. Updating target database." );
                    Update( t.Original.FullName, t.Original.Type, t.Original.Version.ToString() );
                }
            }
        }

        var featureDiff = originalFeatures.Select( o => (o.Name, O: o, F: new VFeature()) )
                            .Concat( finalFeatures.Select( f => (f.Name, O: new VFeature(), F: f) ) )
                            .GroupBy( t => t.Name )
                            .Select( x => (Name: x.Key, x.First().O, x.Last().F) );
        foreach( var f in featureDiff )
        {
            if( !f.O.IsValid )
            {
                monitor.Info( $"Created VFeature: '{f.F}'." );
                Update( f.Name, "VFeature", f.F.Version.ToNormalizedString() );
            }
            else if( !f.F.IsValid )
            {
                monitor.Info( $"Removed VFeature: '{f.O}'." );
                Delete( f.Name, false );
            }
            else if( f.O.Version != f.F.Version )
            {
                monitor.Info( $"Updated VFeature {f.O} to version {f.F.Version}." );
                Update( f.Name, "VFeature", f.F.Version.ToNormalizedString() );
            }
            else monitor.Debug( $"VFeature {f.O} unchanged." );
        }

        if( delete != null )
        {
            delete.Append( ");" );
            // Throws exception on error.
            _manager.ExecuteOneScript( delete.ToString() );
        }
        if( update != null )
        {
            update.Append( ";" ).Append( SqliteVersionedItemReader.MergeTemporaryTableScript );
            // Throws exception on error.
            _manager.ExecuteOneScript( update.ToString() );
        }
    }
}
