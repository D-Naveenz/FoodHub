using Microsoft.Data.SqlClient;
using System.Data;
using System.Text.Json.Nodes;

namespace FoodHub.Core.Services;

/// <summary>Compares physical schema metadata against a separately created historical-baseline database.</summary>
public static class BaselineVerifier
{
    /// <summary>Returns a deterministic schema description, excluding EF history and all application data.</summary>
    public static async Task<string> DescribeAsync(string connectionString, CancellationToken cancellation = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellation);
        await using var command = new SqlCommand(CatalogSql, connection) { CommandTimeout = 30 };
        var json = new System.Text.StringBuilder();
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        while (await reader.ReadAsync(cancellation)) json.Append(reader.GetString(0));
        // SQL clients preserve outer whitespace and line endings in module source.
        // Normalize only those presentation differences, retaining all internal SQL text.
        var document = JsonNode.Parse(json.ToString())!;
        foreach (var procedure in document["procedures"]!.AsArray())
            procedure!["definition"] = procedure["definition"]!.GetValue<string>().ReplaceLineEndings("\n").Trim();
        return document.ToJsonString();
    }

    /// <summary>Aborts on any schema difference rather than guessing whether existing data is compatible.</summary>
    public static async Task VerifyAsync(string target, string reference, CancellationToken cancellation = default)
    {
        if (await DescribeAsync(target, cancellation) != await DescribeAsync(reference, cancellation))
            throw new InvalidOperationException("Schema drift detected. Baseline was not recorded. Compare the target with a clean Baseline database before continuing.");
    }

    // Object IDs are database-local, so comparisons use stable names and ordered definitions instead.
    private const string CatalogSql = """
        SELECT
          JSON_QUERY((SELECT SCHEMA_NAME(t.schema_id) AS [schema], t.name,
            JSON_QUERY((SELECT c.name, c.column_id, TYPE_NAME(c.user_type_id) AS [type], c.max_length, c.precision, c.scale,
              c.is_nullable, c.is_identity, c.collation_name, cc.definition AS computed, cc.is_persisted,
              CONVERT(nvarchar(100), ic.seed_value) AS seed, CONVERT(nvarchar(100), ic.increment_value) AS increment,
              dc.name AS default_name, dc.definition AS default_definition
              FROM sys.columns c LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
              LEFT JOIN sys.identity_columns ic ON ic.object_id=c.object_id AND ic.column_id=c.column_id
              LEFT JOIN sys.default_constraints dc ON dc.object_id=c.default_object_id
              WHERE c.object_id=t.object_id ORDER BY c.column_id FOR JSON PATH, INCLUDE_NULL_VALUES)) AS columns,
            JSON_QUERY((SELECT i.name, i.type, i.is_unique, i.is_primary_key, i.is_unique_constraint, i.filter_definition, i.is_disabled,
              JSON_QUERY((SELECT COL_NAME(x.object_id,x.column_id) AS name, x.key_ordinal, x.is_descending_key, x.is_included_column
                FROM sys.index_columns x WHERE x.object_id=i.object_id AND x.index_id=i.index_id
                ORDER BY x.index_column_id FOR JSON PATH)) AS columns
              FROM sys.indexes i WHERE i.object_id=t.object_id AND i.index_id>0 ORDER BY i.name FOR JSON PATH, INCLUDE_NULL_VALUES)) AS indexes,
            JSON_QUERY((SELECT c.name, c.definition, c.is_disabled, c.is_not_trusted FROM sys.check_constraints c
              WHERE c.parent_object_id=t.object_id ORDER BY c.name FOR JSON PATH)) AS checks,
            JSON_QUERY((SELECT f.name, OBJECT_SCHEMA_NAME(f.referenced_object_id) AS target_schema,
              OBJECT_NAME(f.referenced_object_id) AS target_table, f.delete_referential_action, f.update_referential_action, f.is_disabled, f.is_not_trusted,
              JSON_QUERY((SELECT COL_NAME(x.parent_object_id,x.parent_column_id) AS source_column,
                COL_NAME(x.referenced_object_id,x.referenced_column_id) AS target_column FROM sys.foreign_key_columns x
                WHERE x.constraint_object_id=f.object_id ORDER BY x.constraint_column_id FOR JSON PATH)) AS columns
              FROM sys.foreign_keys f WHERE f.parent_object_id=t.object_id ORDER BY f.name FOR JSON PATH)) AS foreign_keys
            FROM sys.tables t WHERE t.is_ms_shipped=0 AND t.name<>'__EFMigrationsHistory'
            ORDER BY SCHEMA_NAME(t.schema_id),t.name FOR JSON PATH)) AS tables,
          JSON_QUERY((SELECT SCHEMA_NAME(p.schema_id) AS [schema], p.name, m.definition, m.uses_ansi_nulls, m.uses_quoted_identifier
            FROM sys.procedures p JOIN sys.sql_modules m ON p.object_id=m.object_id
            WHERE p.is_ms_shipped=0 ORDER BY SCHEMA_NAME(p.schema_id),p.name FOR JSON PATH)) AS procedures,
          JSON_QUERY((SELECT SCHEMA_NAME(t.schema_id) AS [schema],t.name,
            JSON_QUERY((SELECT c.name, c.column_id, TYPE_NAME(c.user_type_id) AS [type], c.max_length,c.precision,c.scale,c.is_nullable
              FROM sys.columns c WHERE c.object_id=t.type_table_object_id ORDER BY c.column_id FOR JSON PATH)) AS columns,
            JSON_QUERY((SELECT c.definition FROM sys.check_constraints c WHERE c.parent_object_id=t.type_table_object_id ORDER BY c.definition FOR JSON PATH)) AS checks,
            JSON_QUERY((SELECT i.is_unique, i.is_primary_key,
              JSON_QUERY((SELECT COL_NAME(x.object_id,x.column_id) AS name,x.key_ordinal FROM sys.index_columns x
                WHERE x.object_id=i.object_id AND x.index_id=i.index_id ORDER BY x.index_column_id FOR JSON PATH)) AS columns
              FROM sys.indexes i WHERE i.object_id=t.type_table_object_id AND i.index_id>0 ORDER BY i.index_id FOR JSON PATH)) AS indexes
            FROM sys.table_types t ORDER BY SCHEMA_NAME(t.schema_id),t.name FOR JSON PATH)) AS table_types
        FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
        """;
}
