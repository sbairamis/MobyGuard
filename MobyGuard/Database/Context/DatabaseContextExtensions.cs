using Microsoft.EntityFrameworkCore;

namespace MobyGuard.Database.Context;

public static class DatabaseContextExtensions
{
    // Rows inserted with explicit ids or deleted rows can leave SQLite's sqlite_sequence out of sync,
    // so align it with the current highest id in the table.
    public static void ResetIdentitySequence<TEntity>(this DatabaseContext context) where TEntity : class
    {
        var table = context.Model.FindEntityType(typeof(TEntity))!.GetTableName()!;
        var quotedTable = table.Replace("\"", "\"\"");

        var sql = $"UPDATE \"sqlite_sequence\" SET \"seq\" = COALESCE((SELECT MAX(\"Id\") FROM \"{quotedTable}\"), 0) WHERE \"name\" = {{0}};";

        context.Database.ExecuteSqlRaw(sql, table);
    }
}
