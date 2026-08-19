using System.Text.RegularExpressions;
using System.Xml.Linq;
using PpLint.Core;
using PpLint.Core.Model;

namespace PpLint.Extractors;

/// <summary>
/// Preenche o projeto a partir de uma solução exportada: manifesto,
/// tabelas Dataverse, canvas apps aninhados e cloud flows.
/// </summary>
public static class SolutionExtractor
{
    private static readonly Regex GuidSuffix = new(
        @"-[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}$",
        RegexOptions.Compiled);

    public static void Populate(IArtifactSource source, PowerPlatformProject project)
    {
        ReadManifest(source, project);
        ReadEntities(source, project);
        ReadCanvasApps(source, project);
        ReadWorkflows(source, project);
    }

    private static void ReadManifest(IArtifactSource source, PowerPlatformProject project)
    {
        if (!source.Has("solution.xml"))
            return;

        var manifest = TryParse(source, "solution.xml")?.Root?.Element("SolutionManifest");
        if (manifest is null)
            return;

        project.Solution = new SolutionInfo(
            UniqueName: manifest.Element("UniqueName")?.Value ?? string.Empty,
            PublisherPrefix: manifest.Element("Publisher")?.Element("CustomizationPrefix")?.Value ?? string.Empty,
            Version: manifest.Element("Version")?.Value ?? string.Empty,
            Managed: manifest.Element("Managed")?.Value.Trim() == "1");
    }

    private static void ReadEntities(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.StartsWith("Entities/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith("/Entity.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var doc = TryParse(source, entry);
            var entity = doc?.Root?.Element("EntityInfo")?.Element("entity");
            if (entity is null)
                continue;

            var logicalName = entity.Attribute("Name")?.Value
                              ?? doc!.Root!.Element("Name")?.Value
                              ?? string.Empty;

            var columns = new List<DataColumn>();
            foreach (var attribute in entity.Element("attributes")?.Elements("attribute") ?? [])
            {
                var columnLogical = attribute.Element("LogicalName")?.Value
                                    ?? attribute.Element("Name")?.Value;
                if (string.IsNullOrWhiteSpace(columnLogical))
                    continue;

                columns.Add(new DataColumn(
                    LogicalName: columnLogical,
                    SchemaName: attribute.Attribute("PhysicalName")?.Value ?? columnLogical,
                    Type: attribute.Element("Type")?.Value ?? string.Empty,
                    Required: attribute.Element("RequiredLevel")?.Value
                        .Equals("required", StringComparison.OrdinalIgnoreCase) == true));
            }

            project.Tables.Add(new DataTable(
                logicalName,
                logicalName,
                columns,
                new SourceLocation(project.SourcePath, entry, logicalName, 0, 0)));
        }
    }

    private static void ReadCanvasApps(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.EndsWith(".msapp", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var nested = source.OpenNested(entry);
            if (nested is null)
                continue;

            project.Apps.Add(MsappExtractor.Extract(nested, project.SourcePath, AppNameFrom(entry)));
        }
    }

    private static void ReadWorkflows(IArtifactSource source, PowerPlatformProject project)
    {
        var entries = source.Entries
            .Where(e => e.StartsWith("Workflows/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            string json;
            try
            {
                json = source.ReadText(entry);
            }
            catch (ArtifactException)
            {
                continue;
            }

            var flow = FlowExtractor.Extract(json, project.SourcePath, entry, FlowNameFrom(entry));
            if (flow is not null)
                project.Flows.Add(flow);
        }
    }

    /// <summary>"CanvasApps/AppVendas_DocumentUri.msapp" -> "AppVendas".</summary>
    private static string AppNameFrom(string entry)
    {
        var name = Path.GetFileNameWithoutExtension(entry);
        const string suffix = "_DocumentUri";
        return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? name[..^suffix.Length]
            : name;
    }

    /// <summary>"Workflows/AprovarPedido-{GUID}.json" -> "AprovarPedido".</summary>
    private static string FlowNameFrom(string entry) =>
        GuidSuffix.Replace(Path.GetFileNameWithoutExtension(entry), string.Empty);

    private static XDocument? TryParse(IArtifactSource source, string entry)
    {
        try
        {
            return XDocument.Parse(source.ReadText(entry));
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
        catch (ArtifactException)
        {
            return null;
        }
    }
}
