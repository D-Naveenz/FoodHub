using System.Reflection;
using System.Text.RegularExpressions;

namespace FoodHub.Core.Migrations;

/// <summary>Reads the immutable historical SQL baseline embedded in the migration assembly.</summary>
public static class BaselineSql
{
    /// <summary>Splits SQLCMD batch separators without sending GO to SQL Server.</summary>
    public static IEnumerable<string> Batches()
    {
        using var stream = typeof(BaselineSql).Assembly.GetManifestResourceStream("FoodHub.Core.Migrations.Sql.Baseline.sql")
            ?? throw new InvalidOperationException("The baseline SQL resource is missing.");
        using var reader = new StreamReader(stream);
        return Regex.Split(reader.ReadToEnd(), @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)
            .Where(batch => !string.IsNullOrWhiteSpace(batch)).ToArray();
    }
}
