using DesktopOrganizer.Core.Services;
using Microsoft.Win32;

namespace DesktopOrganizer.Infrastructure.Startup;

/// <summary>
/// Implementação de <see cref="IStartupService"/> baseada no Registry do Windows.
/// Grava/lê/remove o valor <c>DesktopOrganizer</c> sob a chave
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>, garantindo que o
/// executável seja iniciado automaticamente no logon do usuário atual.
/// </summary>
/// <remarks>
/// Usa <c>HKCU</c> (não <c>HKLM</c>) porque a aplicação é instalada e configurada
/// por usuário — não requer privilégios elevados (UAC) para escrita.
/// </remarks>
public sealed class RegistryStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DesktopOrganizer";

    public void Enable(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException(
                "Caminho do executável não pode ser vazio.",
                nameof(executablePath));
        }

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException(
                $"Não foi possível abrir/criar a chave de Registry: HKCU\\{RunKeyPath}");

        // Valor entre aspas para suportar caminhos com espaços (ex.: Program Files).
        string quotedPath = $"\"{executablePath}\"";
        key.SetValue(ValueName, quotedPath, RegistryValueKind.String);
    }

    public void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            // Chave não existe: nada a remover. Operação idempotente.
            return;
        }

        // DeleteValue com throwOnMissingValue=false torna o método idempotente
        // mesmo quando o valor já foi removido manualmente.
        key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key is null)
            {
                return false;
            }

            object? value = key.GetValue(ValueName);
            return value is not null;
        }
        catch (Exception)
        {
            // Qualquer falha de acesso ao Registry (permissões, chave corrompida)
            // é tratada como "não habilitado", evitando crashar a aplicação.
            return false;
        }
    }
}
