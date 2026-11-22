using Rop.Database;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// AB-9: ConnStringExtractor must recognize aliases (Server/Database) and must not
/// get confused by key-like text inside quoted values.
/// Tested through the Server/MainDatabaseName properties of AbsDatabase.
/// </summary>
public class ConnStringTests
{
    [Fact]
    public void Ctor_Extracts_Server_And_Database_Aliases()
    {
        var db = new FakeDatabase("Server=myServer;Database=myDb;Integrated Security=True");

        Assert.Equal("myServer", db.Server);
        Assert.Equal("myDb", db.MainDatabaseName);
    }

    [Fact]
    public void Ctor_Extracts_Address_Alias()
    {
        var db = new FakeDatabase("Address=myServer\\SQL1;Initial Catalog=myDb;Trusted_Connection=True");

        Assert.Equal(@"myServer\SQL1", db.Server);
        Assert.Equal("myDb", db.MainDatabaseName);
    }

    [Fact]
    public void Ctor_Ignores_Key_Lookalikes_Inside_Quoted_Values()
    {
        // With the old regex, the first match was "fake" (inside the
        // password) because it came before the real keys.
        var db = new FakeDatabase(
            """Password="data source=fake;initial catalog=fake";Data Source=RealServer;Initial Catalog=RealDb""");

        Assert.Equal("RealServer", db.Server);
        Assert.Equal("RealDb", db.MainDatabaseName);
    }

    [Fact]
    public void Ctor_Standard_Keys_Still_Work()
    {
        var db = new FakeDatabase("Data Source=(localdb)\\MSSQLLocalDB;Initial Catalog=MiBase");

        Assert.Equal(@"(localdb)\MSSQLLocalDB", db.Server);
        Assert.Equal("MiBase", db.MainDatabaseName);
    }
}
