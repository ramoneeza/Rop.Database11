using Dapper;
using Microsoft.Data.SqlClient;

namespace Rop.SqlDatabase11.ChangeTracking.Tests;

/// <summary>
/// Test schemas with Change Tracking enabled at table level (except CtNoTrack).
/// </summary>
internal static class TestTables
{
    public static void CreateCtItems(SqlConnection connection)
    {
        Recreate(connection, "CtItems", """
            CREATE TABLE CtItems (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Quantity INT NOT NULL
            );
            """);
        connection.Execute("ALTER TABLE CtItems ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);");
    }

    public static void CreateCtPartial(SqlConnection connection)
    {
        Recreate(connection, "CtPartial", """
            CREATE TABLE CtPartial (
                OwnerId INT NOT NULL,
                Code NVARCHAR(20) NOT NULL,
                Quantity INT NOT NULL,
                PRIMARY KEY (OwnerId, Code)
            );
            """);
        connection.Execute("ALTER TABLE CtPartial ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);");
    }

    public static void CreateCtNoTrack(SqlConnection connection)
    {
        Recreate(connection, "CtNoTrack", """
            CREATE TABLE CtNoTrack (
                Id INT PRIMARY KEY,
                Name NVARCHAR(50) NOT NULL
            );
            """);
    }

    /// <summary>
    /// CE-7: tabla con palabra reservada ('Order') y Change Tracking activado,
    /// para validar el escapado en 'changetable(changes [Order], ...)'.
    /// </summary>
    public static void CreateCtOrder(SqlConnection connection)
    {
        Recreate(connection, "[Order]", """
            CREATE TABLE [Order] (
                [Id] INT PRIMARY KEY,
                [User] NVARCHAR(100) NOT NULL,
                [Quantity] INT NOT NULL
            );
            """);
        connection.Execute("ALTER TABLE [Order] ENABLE CHANGE_TRACKING WITH (TRACK_COLUMNS_UPDATED = ON);");
    }

    public static void CreateCtTypes(SqlConnection connection)
    {
        Recreate(connection, "CtTypes", """
            CREATE TABLE CtTypes (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                D DATE NULL,
                D2 DATE NULL,
                T TIME NULL,
                T2 TIME NULL
            );
            """);
    }

    private static void Recreate(SqlConnection connection, string tableName, string createSql)
    {
        connection.Execute($"IF OBJECT_ID('{tableName}') IS NOT NULL DROP TABLE {tableName};");
        connection.Execute(createSql);
    }
}
