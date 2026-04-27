using System.Diagnostics;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Win32;

/// <summary>
/// Implementação de <see cref="IWindowRestoreService"/> baseada em Win32.
///
/// Escopo desta TASK-012: APENAS detecção de processo já em execução, lançamento
/// via <see cref="Process.Start(ProcessStartInfo)"/> e polling até a janela
/// principal aparecer (MainWindowHandle != IntPtr.Zero) ou o
/// <see cref="CancellationToken"/> ser cancelado.
///
/// Reposicionamento (SetWindowPos / ShowWindow) e movimento entre virtual
/// desktops (IVirtualDesktopManager.MoveWindowToDesktop) ficam para TASK-013 —
/// NÃO implementar aqui.
///
/// Fluxo:
/// 1. Procurar processo existente cujo <c>MainModule.FileName</c> bata
///    case-insensitive com <see cref="WindowEntry.ExecutablePath"/>; se houver
///    e o título da janela principal contiver/coincidir parcialmente com
///    <see cref="WindowEntry.WindowTitle"/>, considerar achado.
/// 2. Caso contrário: validar <see cref="File.Exists"/> e iniciar processo.
/// 3. Polling em <c>Task.Delay(500ms)</c> com <c>process.Refresh()</c> entre
///    iterações até obter um MainWindowHandle válido ou cancelamento.
/// </summary>
internal sealed class Win32WindowRestoreService : IWindowRestoreService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    public async Task<RestoreResult> RestoreWindowAsync(WindowEntry entry, CancellationToken ct)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        // 1. Tentar localizar processo já em execução com janela compatível.
        var existing = TryFindExistingProcess(entry);
        if (existing is not null)
        {
            // TASK-013 fará o reposicionamento. Aqui apenas reportamos sucesso.
            return RestoreResult.Success("Window already open");
        }

        // 2. Não encontrado — validar existência do executável antes de tentar lançar.
        if (string.IsNullOrWhiteSpace(entry.ExecutablePath) || !File.Exists(entry.ExecutablePath))
        {
            return RestoreResult.Failed(
                RestoreFailureCode.ExecutableNotFound,
                $"Executable not found: {entry.ExecutablePath}");
        }

        Process? launched;
        try
        {
            launched = Process.Start(new ProcessStartInfo
            {
                FileName = entry.ExecutablePath,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            return RestoreResult.Failed(
                RestoreFailureCode.LaunchFailed,
                $"Failed to launch '{entry.ExecutablePath}': {ex.Message}");
        }

        if (launched is null)
        {
            return RestoreResult.Failed(
                RestoreFailureCode.LaunchFailed,
                $"Process.Start returned null for '{entry.ExecutablePath}'");
        }

        // 3. Polling até MainWindowHandle aparecer ou cancelar.
        try
        {
            while (true)
            {
                if (ct.IsCancellationRequested)
                {
                    return RestoreResult.Failed(
                        RestoreFailureCode.WindowNotFound,
                        "Cancelled before main window appeared");
                }

                try
                {
                    launched.Refresh();
                }
                catch
                {
                    // processo pode ter saído; continuar — próxima leitura decidirá.
                }

                IntPtr handle;
                try
                {
                    handle = launched.MainWindowHandle;
                }
                catch
                {
                    handle = IntPtr.Zero;
                }

                if (handle != IntPtr.Zero)
                {
                    // TASK-013 fará reposicionamento + virtual desktop.
                    return RestoreResult.Success("Window launched");
                }

                try
                {
                    await Task.Delay(PollInterval, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return RestoreResult.Failed(
                        RestoreFailureCode.WindowNotFound,
                        "Cancelled while waiting for main window");
                }
            }
        }
        finally
        {
            // Não fazemos Dispose do Process aqui: o handle pertence ao SO e o
            // próprio processo segue vivo. Process.Dispose apenas fecha o
            // handle gerenciado, mas evitamos manter referência.
            launched.Dispose();
        }
    }

    /// <summary>
    /// Enumera <see cref="Process.GetProcesses"/> tentando casar pelo caminho
    /// completo do executável (case-insensitive). <c>MainModule.FileName</c>
    /// pode lançar <see cref="System.ComponentModel.Win32Exception"/> quando o
    /// processo é elevado/protegido — nestes casos ignoramos silenciosamente.
    /// </summary>
    private static Process? TryFindExistingProcess(WindowEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ExecutablePath))
        {
            return null;
        }

        var targetPath = entry.ExecutablePath;
        var targetTitle = entry.WindowTitle ?? string.Empty;

        Process[] all;
        try
        {
            all = Process.GetProcesses();
        }
        catch
        {
            return null;
        }

        foreach (var p in all)
        {
            string? path = null;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch
            {
                // Access Denied / processos protegidos — ignorar.
            }

            if (string.IsNullOrEmpty(path))
            {
                p.Dispose();
                continue;
            }

            if (!string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                p.Dispose();
                continue;
            }

            string title;
            try
            {
                title = p.MainWindowTitle ?? string.Empty;
            }
            catch
            {
                title = string.Empty;
            }

            // Matching parcial case-insensitive: título capturado contido no
            // título atual OU vice-versa (para tolerar pequenas mudanças).
            // Quando WindowTitle capturado está vazio, basta o path bater.
            var matches = string.IsNullOrEmpty(targetTitle)
                || title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase)
                || targetTitle.Contains(title, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                return p; // caller não precisa do handle por ora; descartado depois pelo GC
            }

            p.Dispose();
        }

        return null;
    }
}
