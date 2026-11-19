using Dapper;
using Microsoft.Data.SqlClient;

namespace Rop.SqlDatabase11.Tests;

/// <summary>
/// Test schemas. Each method drops and recreates its table so tests start clean.
/// </summary>
internal static class TestTables
{
    public static void CreateItemsIntAuto(SqlConnection connection) =>
        Recreate(connection, "ItemsIntAuto", """
            CREATE TABLE ItemsIntAuto (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Quantity INT NOT NULL
            );
            """);

    public static void CreateItemsStringKey(SqlConnection connection) =>
        Recreate(connection, "ItemsStringKey", """
            CREATE TABLE ItemsStringKey (
                Id NVARCHAR(50) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL,
                Quantity INT NOT NULL
            );
            """);

    public static void CreateLogEntries(SqlConnection connection) =>
        Recreate(connection, "LogEntries", """
            CREATE TABLE LogEntries (
                Message NVARCHAR(200) NOT NULL,
                Severity INT NOT NULL
            );
            """);

    public static void CreateItemsPartial(SqlConnection connection) =>
        Recreate(connection, "ItemsPartial", """
            CREATE TABLE ItemsPartial (
                OwnerId INT NOT NULL,
                Code NVARCHAR(20) NOT NULL,
                Quantity INT NOT NULL,
                CreatedAt DATETIME2 NOT NULL DEFAULT SYSDATETIME(),
                PRIMARY KEY (OwnerId, Code)
            );
            """);

    /// <summary>
    /// CE-7: tabla y columna con palabras reservadas ('Order' / 'User').
    /// </summary>
    public static void CreateReservedOrder(SqlConnection connection) =>
        Recreate(connection, "[Order]", """
            CREATE TABLE [Order] (
                [Id] INT PRIMARY KEY,
                [User] NVARCHAR(100) NOT NULL,
                [Quantity] INT NOT NULL
            );
            """);

    public static void CreateOrders(SqlConnection connection)
    {
        Recreate(connection, "Orders", """
            CREATE TABLE Orders (
                Id INT IDENTITY(1,1) PRIMARY KEY,
                Name NVARCHAR(100) NOT NULL
            );
            """);
        Recreate(connection, "OrderLines", """
            CREATE TABLE OrderLines (
                Id INT PRIMARY KEY,
                OrderId INT NOT NULL,
                Product NVARCHAR(100) NOT NULL
            );
            """);
    }

    private static void Recreate(SqlConnection connection, string tableName, string createSql)
    {
        connection.Execute($"IF OBJECT_ID('{tableName}') IS NOT NULL DROP TABLE {tableName};");
        connection.Execute(createSql);
    }
}
