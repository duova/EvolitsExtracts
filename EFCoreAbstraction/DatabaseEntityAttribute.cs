namespace Evolits.Common;

/// <summary>
/// Marker for classes that should have a corresponding database table.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class DatabaseEntityAttribute : Attribute
{
    /// <summary>
    /// Used to identify which database interface this belongs to.
    /// </summary>
    public Type? DatabaseInterfaceType { get; set; }
}