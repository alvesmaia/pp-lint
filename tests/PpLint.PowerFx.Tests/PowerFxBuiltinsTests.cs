namespace PpLint.PowerFx.Tests;

public class PowerFxBuiltinsTests
{
    [Theory]
    [InlineData("If")]
    [InlineData("Set")]
    [InlineData("Filter")]
    [InlineData("Notify")]
    [InlineData("IsBlank")]
    public void KnownFunctionsAreBuiltin(string name) =>
        Assert.Contains(name, PowerFxBuiltins.FunctionNames);

    [Fact]
    public void FunctionNamesComeFromTheEngine()
    {
        // Se a lista viesse hardcoded, envelheceria a cada atualização do pacote.
        Assert.True(PowerFxBuiltins.FunctionNames.Count > 100,
            $"esperava centenas de funções, veio {PowerFxBuiltins.FunctionNames.Count}");
    }

    [Theory]
    [InlineData("Color")]
    [InlineData("Align")]
    [InlineData("Font")]
    [InlineData("DisplayMode")]
    [InlineData("ScreenTransition")]
    [InlineData("SortOrder")]
    public void KnownEnumsAreBuiltin(string name) =>
        Assert.Contains(name, PowerFxBuiltins.EnumNames);

    [Fact]
    public void IsBuiltinCoversFunctionsAndEnums()
    {
        Assert.True(PowerFxBuiltins.IsBuiltin("Filter"));
        Assert.True(PowerFxBuiltins.IsBuiltin("Color"));
        Assert.False(PowerFxBuiltins.IsBuiltin("varTotal"));
    }

    [Fact]
    public void LookupIsCaseInsensitive()
    {
        // Power Fx não diferencia maiúsculas em nomes de função.
        Assert.True(PowerFxBuiltins.IsBuiltin("filter"));
        Assert.True(PowerFxBuiltins.IsBuiltin("COLOR"));
    }
}
