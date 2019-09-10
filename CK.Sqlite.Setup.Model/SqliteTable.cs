using CK.Core;

namespace CK.Sqlite
{
    /// <summary>
    /// Base class for table objects. 
    /// Unless marked with <see cref="AutoRealDefiner"/>, direct specializations are de facto ambient objects.
    /// A table is a <see cref="SqlPackage"/> with a <see cref="TableName"/>.
    /// </summary>
    [AutoRealDefiner]
    public class SqliteTable : SqlitePackage
    {
        /// <summary>
        /// Gets the table name.
        /// </summary>
        public string TableName { get; protected set; }
    }
}
