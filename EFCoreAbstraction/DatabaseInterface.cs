using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Evolits.Common;

//DatabaseInterface must be extended in a class inside the assembly being compiled in this exact way for EF to register it.
//CustomDatabaseInterface should be replaced with the name of the database followed by DatabaseInterface.
/*
using Evolits.ServerLib;

public partial class CustomDatabaseInterface : DatabaseInterface
{
    public CustomDatabaseInterface() : base("", "", "", "")
    {
    }
    
    public CustomDatabaseInterface(string host, string database, string
        username, string password) : base(host, database, username,
        password)
    {
    }
}
*/

//This defines a class to be used with a certain interface.
//A string Id is required with every entity.
//Suffix every class name with Entity.
/*
using Evolits.ServerLib;

[DatabaseEntity(DatabaseInterfaceType = typeof(CustomDatabaseInterface))]
public class ItemEntity
{
    public string Id { get; set; }

    public int TestItem { get; set; }
}
*/

/// <summary>
/// Extension of EF core DbContext to facilitate model attribute marking.
/// "dotnet ef migrations add InitialCreate" must be run in .NET CLI as setup.
/// If any entity is updated "dotnet ef migrations add [name]" must be ran in .NET CLI to generate migrations.
/// Migrations should be done with SQL scripts ran with an updater application on a CI pipeline.
/// </summary>
public abstract class DatabaseInterface : DbContext
{
    private readonly Type _thisType;
    
    private readonly string _hostString;

    private readonly string _databaseString;

    private readonly string _usernameString;

    private readonly string _passwordString;

    /// <summary>
    /// Builds a database context based on model classes marked with the DatabaseEntity attribute.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="database"></param>
    /// <param name="username"></param>
    /// <param name="password"></param>
    public DatabaseInterface(string host, string database, string username, string password)
    {
        _thisType = this.GetType();
        _hostString = host;
        _databaseString = database;
        _usernameString = username;
        _passwordString = password;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) => optionsBuilder.UseNpgsql("Host=" +
        _hostString + ";Database=" + _databaseString + ";Username=" + _usernameString + ";Password=" + _passwordString);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        //Add database entities that belong to this interface.
        foreach (Type type in _thisType.Assembly.GetTypes())
        {
            IEnumerable<System.Attribute> attributes = type.GetCustomAttributes(typeof(DatabaseEntityAttribute));
            if (attributes.Count() < 1) continue;
            if (((DatabaseEntityAttribute)attributes.First()).DatabaseInterfaceType != _thisType) continue;
            modelBuilder.Entity(type);
        }
    }
}