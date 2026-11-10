using Microsoft.EntityFrameworkCore;

namespace MobyGuard.Database.Context;

public static class DatabaseContextExtensions
{
    // Rows inserted with explicit ids don't advance PostgreSQL's identity sequence,
    // so move it past the highest id or the next insert collides with an existing row.
    public static void ResetIdentitySequence<TEntity>(this DatabaseContext context) where TEntity : class
    {
        var table = context.Model.FindEntityType(typeof(TEntity))!.GetTableName();

        var sql = $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), COALESCE((SELECT MAX(\"Id\") FROM \"{table}\"), 0) + 1, false);";

        context.Database.ExecuteSqlRaw(sql);
    }
}
