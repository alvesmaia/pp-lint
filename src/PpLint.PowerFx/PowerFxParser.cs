using System.Globalization;
using Microsoft.PowerFx;
using Microsoft.PowerFx.Syntax;

namespace PpLint.PowerFx;

public sealed record FxParseResult(bool IsSuccess, TexlNode? Root, IReadOnlyList<string> Errors);

/// <summary>
/// Fachada sobre o parser oficial do Power Fx. Nunca lança: uma expressão
/// que não compila vira um resultado com IsSuccess = false, para que a
/// análise do restante do app continue.
/// </summary>
public static class PowerFxParser
{
    private static readonly Engine SharedEngine = new(new PowerFxConfig());

    /// <summary>
    /// Cultura invariante porque o .msapp guarda InvariantScript — vírgula separa
    /// argumentos, independentemente da cultura da máquina que roda o linter.
    /// AllowsSideEffects habilita o operador de encadeamento ';', usado em toda
    /// propriedade de comportamento (OnSelect, OnVisible, OnStart).
    /// </summary>
    private static readonly ParserOptions Options = new()
    {
        Culture = CultureInfo.InvariantCulture,
        AllowsSideEffects = true,
    };

    public static FxParseResult Parse(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
            return new FxParseResult(true, null, []);

        try
        {
            var result = SharedEngine.Parse(script, Options);
            var errors = result.Errors?.Select(e => e.Message).ToList() ?? [];

            return result.IsSuccess
                ? new FxParseResult(true, result.Root, [])
                : new FxParseResult(false, null, errors.Count > 0 ? errors : ["Erro de sintaxe."]);
        }
        catch (Exception ex)
        {
            // Defensivo: nenhuma expressão de usuário pode derrubar a análise.
            return new FxParseResult(false, null, [ex.Message]);
        }
    }
}
