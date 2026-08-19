using PpLint.Core.Configuration;

namespace PpLint.Core.Tests;

public class ConfigLocatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"pplint-cfg-{Guid.NewGuid():N}");

    public ConfigLocatorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void FindsFileInTheStartDirectory()
    {
        var file = Path.Combine(_root, "pp-lint.toml");
        File.WriteAllText(file, "[pp-lint]");

        Assert.Equal(file, ConfigLocator.Find(_root));
    }

    [Fact]
    public void FindsFileInAnAncestor()
    {
        var file = Path.Combine(_root, "pp-lint.toml");
        File.WriteAllText(file, "[pp-lint]");

        var deep = Path.Combine(_root, "src", "apps");
        Directory.CreateDirectory(deep);

        Assert.Equal(file, ConfigLocator.Find(deep));
    }

    [Fact]
    public void NearestAncestorWins()
    {
        File.WriteAllText(Path.Combine(_root, "pp-lint.toml"), "[pp-lint]");

        var nested = Path.Combine(_root, "src");
        Directory.CreateDirectory(nested);
        var nearer = Path.Combine(nested, "pp-lint.toml");
        File.WriteAllText(nearer, "[pp-lint]");

        Assert.Equal(nearer, ConfigLocator.Find(nested));
    }

    [Fact]
    public void ReturnsNullWhenThereIsNoFile()
    {
        var empty = Path.Combine(_root, "vazio");
        Directory.CreateDirectory(empty);

        // Pode existir um pp-lint.toml acima do temp em teoria; o teste usa um
        // diretório recém-criado sob o temp, onde isso não acontece na prática.
        Assert.Null(ConfigLocator.Find(empty));
    }

    [Fact]
    public void MissingDirectoryReturnsNull()
    {
        Assert.Null(ConfigLocator.Find(Path.Combine(_root, "nao-existe")));
    }
}
