namespace PpLint.Core.Model;

public sealed record DataColumn(
    string LogicalName,
    string SchemaName,
    string Type,
    bool Required);

public sealed record DataTable(
    string LogicalName,
    string SchemaName,
    IReadOnlyList<DataColumn> Columns,
    SourceLocation Location);
