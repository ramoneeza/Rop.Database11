using Microsoft.Data.SqlClient;
using SqlDb = Rop.Database11.Database;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// SD-1: FactoryExternalDatabase must change ONLY InitialCatalog (and
/// DataSource in the server overload) without corrupting the rest of the
/// connection string, even when the DB name appears in server/user/password.
/// No database needed: the connection string is only built and inspected.
/// </summary>
public class FactoryExternalDatabaseTests
{
    private const string ConnStr =
        "Data Source=MyDbServer;Initial Catalog=MyDb;User Id=MyDbUser;Password=MyDbPass";

    [Fact]
    public void FactoryExternalDatabase_Only_Changes_InitialCatalog()
    {
        var parent = new SqlDb(ConnStr);
        Assert.Equal("MyDb", parent.MainDatabaseName);

        var external = parent.FactoryExternalDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(external.FactoryConnection().ConnectionString);
        Assert.Equal("OtherDb", builder.InitialCatalog);
        Assert.Equal("MyDbServer", builder.DataSource);
        Assert.Equal("MyDbUser", builder.UserID);
        Assert.Equal("MyDbPass", builder.Password);
    }

    [Fact]
    public void FactoryExternalDatabase_With_Server_Changes_Both_And_Preserves_Rest()
    {
        var parent = new SqlDb(ConnStr);

        var external = parent.FactoryExternalDatabase("OtherDb", "OtherServer");

        var builder = new SqlConnectionStringBuilder(external.FactoryConnection().ConnectionString);
        Assert.Equal("OtherDb", builder.InitialCatalog);
        Assert.Equal("OtherServer", builder.DataSource);
        Assert.Equal("MyDbUser", builder.UserID);
        Assert.Equal("MyDbPass", builder.Password);
    }

    [Fact]
    public void FactoryExternalDatabase_With_Server_Aliases_Works_End_To_End()
    {
        // AB-9: the extractor recognizes aliases; SD-1: the builder preserves them.
        var parent = new SqlDb("Server=AliasServer;Database=AliasDb;User Id=U;Password=P");
        Assert.Equal("AliasServer", parent.Server);
        Assert.Equal("AliasDb", parent.MainDatabaseName);

        var external = parent.FactoryExternalDatabase("OtherDb");

        var builder = new SqlConnectionStringBuilder(external.FactoryConnection().ConnectionString);
        Assert.Equal("OtherDb", builder.InitialCatalog);
        Assert.Equal("AliasServer", builder.DataSource);
        Assert.Equal("U", builder.UserID);
        Assert.Equal("P", builder.Password);
    }

    [Fact]
    public void FactoryExternalDatabase_Returns_Cached_Instance()
    {
        var parent = new SqlDb(ConnStr);

        var first = parent.FactoryExternalDatabase("OtherDb");
        var second = parent.FactoryExternalDatabase("OtherDb");

        Assert.Same(first, second);
    }

    [Fact]
    public void FactoryExternalDatabase_With_Server_Caches_By_Db_And_Server()
    {
        // SD-18: previously each call with a different server created a new
        // root (with its own cycle tasks). Now it is cached by (db, server).
        var parent = new SqlDb(ConnStr);

        var a = parent.FactoryExternalDatabase("OtherDb", "OtherServer");
        var b = parent.FactoryExternalDatabase("OtherDb", "OtherServer");
        var c = parent.FactoryExternalDatabase("OtherDb", "ThirdServer");

        Assert.Same(a, b);
        Assert.NotSame(a, c);
    }

    [Fact]
    public void FactoryExternalDatabase_Same_Server_Delegates_To_Cached_Instance()
    {
        var parent = new SqlDb(ConnStr);

        var a = parent.FactoryExternalDatabase("OtherDb");
        var b = parent.FactoryExternalDatabase("OtherDb", parent.Server);

        Assert.Same(a, b);
    }

    [Fact]
    public void FactoryExternalDatabaseT_Type_Without_Key_Throws_InvalidOperationException()
    {
        // GE-3: programming error -> exception with a concrete type.
        // (GetKeyDescription throws InvalidOperationException for keyless
        // types; GetAnyKeyDescription is the variant that returns null.)
        var parent = new SqlDb(ConnStr);

        Assert.Throws<InvalidOperationException>(() => parent.FactoryExternalDatabase<KeylessType>());
        Assert.Throws<InvalidOperationException>(() => parent.FactoryConnection<KeylessType>());
    }

    private sealed class KeylessType
    {
        public int X { get; set; }
    }
}
