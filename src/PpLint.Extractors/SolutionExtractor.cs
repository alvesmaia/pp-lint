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
        // O .zip exportado traz solution.xml na raiz; o pac solution unpack grava
        // Other/Solution.xml, e é esse o formato que os times versionam.
        var entrada = new[] { "solution.xml", "Other/Solution.xml" }
            .FirstOrDefault(source.Has);

        if (entrada is null)
            return;

        var manifest = TryParse(source, entrada)?.Root?.Element("SolutionManifest");
        if (manifest is null)
            return;

        project.Solution = new SolutionInfo(
            UniqueName: manifest.Element("UniqueName")?.Value ?? string.Empty,
            PublisherPrefix: manifest.Element("Publisher")?.Element("CustomizationPrefix")?.Value ?? string.Empty,
            Version: manifest.Element("Version")?.Value ?? string.Empty,
            Managed: manifest.Element("Managed")?.Value.Trim() == "1");
    }

    /// <summary>
    /// Cada elemento &lt;Entity&gt; disponível, com a entrada de onde veio — para
    /// que o achado aponte para o arquivo certo em qualquer dos dois formatos.
    /// </summary>
    private static IEnumerable<(XElement Root, string Entry)> EntityRoots(IArtifactSource source)
    {
        var entries = source.Entries
            .Where(e => e.StartsWith("Entities/", StringComparison.OrdinalIgnoreCase)
                        && e.EndsWith("/Entity.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var root = TryParse(source, entry)?.Root;
            if (root is not null)
                yield return (root, entry);
        }

        // No .zip exportado as tabelas vivem aqui; no formato descompactado esta
        // seção vem vazia, e o laço acima é que produz as entidades.
        var customizations = new[] { "customizations.xml", "Other/Customizations.xml" }
            .FirstOrDefault(source.Has);

        if (customizations is null)
            yield break;

        var lista = TryParse(source, customizations)?.Root?.Element("Entities");
        if (lista is null)
            yield break;

        foreach (var entity in lista.Elements("Entity"))
            yield return (entity, customizations);
    }

    private static void ReadEntities(IArtifactSource source, PowerPlatformProject project)
    {
        // Os dois formatos que existem na prática: o .zip exportado carrega as
        // tabelas dentro de customizations.xml, e o pac solution unpack as separa
        // em Entities/<nome>/Entity.xml. A estrutura de cada <Entity> é a mesma
        // nos dois — o unpack só move o XML para arquivos.
        foreach (var (raiz, entry) in EntityRoots(source))
        {
            var entity = raiz.Element("EntityInfo")?.Element("entity");
            if (entity is null)
                continue;

            var doc = raiz;

            // O Entity.xml traz o nome na forma de esquema (gmx_Expense). No Dataverse
            // o nome lógico é sempre a versão minúscula dele, e é por esse nome que
            // fórmulas e fluxos referenciam a tabela.
            var schemaName = entity.Attribute("Name")?.Value
                             ?? doc.Element("Name")?.Value
                             ?? string.Empty;
            var logicalName = schemaName.ToLowerInvariant();

            var columns = new List<DataColumn>();
            foreach (var attribute in entity.Element("attributes")?.Elements("attribute") ?? [])
            {
                var columnLogical = attribute.Element("LogicalName")?.Value
                                    ?? attribute.Element("Name")?.Value;
                if (string.IsNullOrWhiteSpace(columnLogical))
                    continue;

                var derivada = attribute.Element("CalculationOf")?.Value;

                columns.Add(new DataColumn(
                    LogicalName: columnLogical,
                    SchemaName: attribute.Attribute("PhysicalName")?.Value ?? columnLogical,
                    Type: attribute.Element("Type")?.Value ?? string.Empty,
                    Required: attribute.Element("RequiredLevel")?.Value
                        .Equals("required", StringComparison.OrdinalIgnoreCase) == true)
                {
                    IsCustom = attribute.Element("IsCustomField")?.Value.Trim() == "1",
                    DerivedFrom = string.IsNullOrWhiteSpace(derivada) ? null : derivada,
                    DisplayName = attribute.Element("displaynames")
                        ?.Elements("displayname").FirstOrDefault()
                        ?.Attribute("description")?.Value,
                });
            }

            project.Tables.Add(new DataTable(
                logicalName,
                schemaName,
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

            project.Apps.Add(MsappExtractor.Extract(nested, project.SourcePath, AppNameFrom(entry), entryPrefix: entry));
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
