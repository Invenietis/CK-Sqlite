using CK.Core;
using CK.Setup;
using System.Linq;
using Yodii.Script;

namespace CK.Sqlite.Setup
{
    /// <summary>
    /// Sql package item.
    /// </summary>
    public class SqlitePackageBaseItem : StObjDynamicPackageItem
    {
        /// <summary>
        /// Initializes a new <see cref="SqlitePackageBaseItem"/> bound to a StObj.
        /// </summary>
        /// <param name="monitor">Monitor to use.</param>
        /// <param name="data">Structured Object data that contains the <see cref="Object"/>.</param>
        public SqlitePackageBaseItem( IActivityMonitor monitor, IStObjSetupData data )
            : base( monitor, data )
        {
            Context = data.StObj.StObjMap.MapName;
            SqlitePackageBase p = ActualObject;
            if( p.Database != null ) Location = p.Database.Name;
            ResourceLocation = (ResourceLocator)data.StObj.GetStObjProperty( "ResourceLocation" );
            // By default, a Sql package always has a an associated Model package.
            // If HasModel is not defined (ie. GetStObjProperty returned System.Type.Missing) or not a boolean or true, we do it.
            // Only HasModel = false will prevent us to associate a model.
            object hasModel = data.StObj.GetStObjProperty( "HasModel" );
            if( !(hasModel is bool) || (bool)hasModel ) EnsureModelPackage();

            if( !typeof( SqlitePackageBaseItemDriver ).IsAssignableFrom( data.DriverType ) )
            {
                monitor.Warn( $"Driver type for {FullName} is '{data.DriverType}' that does not specialize '{typeof( SqlitePackageBaseItemDriver ).FullName}'. Since this driver handles scripts application, this package may not behave as a normal Sql package object." );
            }
            Name = data.FullNameWithoutContext;
        }

        /// <summary>
        /// Masked to formally be associated to <see cref="SqlPackageBase"/>.
        /// </summary>
        public new SqlitePackageBase ActualObject => (SqlitePackageBase)base.ActualObject;

        /// <summary>
        /// Gets or sets a <see cref="ResourceLocation"/> that locates the resources associated 
        /// to this package.
        /// </summary>
        public ResourceLocator ResourceLocation { get; set; }

        /// <summary>
        /// Adds any <see cref="SqlDatabaseItem"/> groups to which this package belongs
        /// to the <see cref="DynamicPackageItem.ObjectsPackage"/>'s groups (it it exists).
        /// </summary>
        /// <param name="m">The monitor to use.</param>
        /// <returns>The driver to use.</returns>
        protected override object StartDependencySort( IActivityMonitor m )
        {
            if( ObjectsPackage != null )
            {
                ObjectsPackage.Groups.AddRange( Groups.OfType<SqliteDatabaseItem>() );
            }
            return base.StartDependencySort( m );
        }

        /// <summary>
        /// Processes a Y4 template. Whenever <paramref name="driver"/>, <paramref name="setupItem"/> and <paramref name="model"/>
        /// are not null they are published to the script as Driver, SetupItem and Model global objects.
        /// </summary>
        /// <param name="monitor">The monitor to use.</param>
        /// <param name="driver">The driver (can be null).</param>
        /// <param name="setupItem">The setup item (can be null). </param>
        /// <param name="model">
        /// The model object. When null and if setupItem is a <see cref="ISetupObjectItem"/>, then the model 
        /// is set to be the <see cref="ISetupObjectItem.ActualObject"/>.
        /// </param>
        /// <param name="fileName">The filename (for logging).</param>
        /// <param name="text">The script text.</param>
        /// <returns>The processed text or null if an error occurred and has been logged.</returns>
        public static string ProcessY4Template(
            IActivityMonitor monitor,
            SetupItemDriver driver,
            ISetupItem setupItem,
            object model,
            string fileName,
            string text )
        {
            using( monitor.OpenInfo( $"Evaluating template '{fileName}'." ) )
            {
                GlobalContext c = new GlobalContext();
                if( driver != null ) c.Register( "Driver", driver );
                if( setupItem != null ) c.Register( "SetupItem", setupItem );
                if( model == null )
                {
                    var sO = setupItem as ISetupObjectItem;
                    if( sO != null ) model = sO.ActualObject;
                }
                if( model != null ) c.Register( "Model", model );
                TemplateEngine e = new TemplateEngine( c );
                var r = e.Process( text );
                if( r.ErrorMessage != null )
                {
                    using( monitor.OpenError( r.ErrorMessage ) )
                    {
                        monitor.Trace( text );
                    }
                    return null;
                }
                text = r.Text;
            }
            return text;
        }
    }
}
