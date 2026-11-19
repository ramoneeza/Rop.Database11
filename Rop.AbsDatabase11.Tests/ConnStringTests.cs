using Rop.Database;

namespace Rop.AbsDatabase11.Tests;

/// <summary>
/// AB-9: ConnStringExtractor debe reconocer alias (Server/Database) y no
/// confundirse con texto que parezca clave dentro de valores entrecomillados.
/// Se prueba a traves de las propiedades Server/MainDatabaseName de AbsDatabase.
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
        // Con el regex antiguo, la primera coincidencia era "fake" (dentro del
        // password) porque iba antes que las claves reales.
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
