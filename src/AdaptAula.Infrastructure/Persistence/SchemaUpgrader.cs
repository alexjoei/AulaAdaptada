using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AdaptAula.Infrastructure.Persistence;

/// <summary>
/// Additive schema evolution for the SQLite database. The app has no EF migrations (it relies on
/// <c>EnsureCreated</c>), which never touches an already-existing database — so a deployed database would keep its old
/// tables and the new V2 columns/tables would simply be missing. This creates any missing table and adds any missing
/// column (never drops or rewrites anything), so existing teacher data survives an update.
/// </summary>
public static class SchemaUpgrader
{
    public static void Upgrade(AdaptAulaDbContext db)
    {
        if (db.Database.EnsureCreated()) return; // brand-new database: everything was just created

        // 1) tables / indexes that don't exist yet
        var script = db.Database.GenerateCreateScript();
        foreach (var statement in SplitStatements(script))
        {
            var sql = statement
                .Replace("CREATE TABLE ", "CREATE TABLE IF NOT EXISTS ", StringComparison.Ordinal)
                .Replace("CREATE UNIQUE INDEX ", "CREATE UNIQUE INDEX IF NOT EXISTS ", StringComparison.Ordinal)
                .Replace("CREATE INDEX ", "CREATE INDEX IF NOT EXISTS ", StringComparison.Ordinal);
            db.Database.ExecuteSqlRaw(sql.Replace("{", "{{").Replace("}", "}}"));
        }

        // 2) columns that don't exist yet in tables that do
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null) continue;

            var existing = ExistingColumns(db, table);
            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName();
                if (existing.Contains(column)) continue;

                var type = property.GetColumnType();
                var notNull = !property.IsNullable;
                var sql = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}" +
                          (notNull ? $" NOT NULL DEFAULT {DefaultLiteral(property)}" : "");
                db.Database.ExecuteSqlRaw(sql.Replace("{", "{{").Replace("}", "}}"));
            }
        }
    }

    private static HashSet<string> ExistingColumns(AdaptAulaDbContext db, string table)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connection = db.Database.GetDbConnection();
        var wasClosed = connection.State != System.Data.ConnectionState.Open;
        if (wasClosed) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = command.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }
        finally
        {
            if (wasClosed) connection.Close();
        }
        return columns;
    }

    /// <summary>SQLite cannot add a NOT NULL column without a default, so pick one that deserialises to "empty":
    /// 0/false for numbers and flags, "" for text, [] / {} for the JSON-converted collections and objects.</summary>
    private static string DefaultLiteral(IProperty property)
    {
        var clr = property.ClrType;
        var converterTarget = property.GetValueConverter()?.ProviderClrType;
        if (converterTarget == typeof(string) && clr != typeof(string))
        {
            var isCollection = clr.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(clr) &&
                               !typeof(System.Collections.IDictionary).IsAssignableFrom(clr);
            return isCollection ? "'[]'" : "'{}'";
        }

        var underlying = Nullable.GetUnderlyingType(clr) ?? clr;
        if (underlying == typeof(string)) return "''";
        if (underlying == typeof(Guid)) return "'00000000-0000-0000-0000-000000000000'";
        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset)) return "'0001-01-01 00:00:00'";
        return "0";
    }

    private static IEnumerable<string> SplitStatements(string script) =>
        script.Split(new[] { ";\r\n", ";\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);
}
