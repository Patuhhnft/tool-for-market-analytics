using System.Runtime.CompilerServices;

namespace LeitorMercadoLivre.Infrastructure.Tests;

internal static class RepositoryRoot
{
    /// <summary>
    /// Raiz do repositório a partir do arquivo-FONTE, não da pasta do binário: o teste acha a
    /// pasta config/ mesmo com saída de build redirecionada (CI, pasta temporária).
    /// </summary>
    public static string Find([CallerFilePath] string sourceFile = "")
    {
        var directory = Path.GetDirectoryName(sourceFile);
        while (directory is not null && !File.Exists(Path.Combine(directory, "LeitorMercadoLivre.slnx")))
        {
            directory = Path.GetDirectoryName(directory);
        }

        return directory ?? throw new InvalidOperationException($"LeitorMercadoLivre.slnx não encontrado acima de {sourceFile}.");
    }
}
