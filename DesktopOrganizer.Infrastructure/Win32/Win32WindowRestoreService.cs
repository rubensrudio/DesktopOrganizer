using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Win32;

/// <summary>
/// Implementação de <see cref="IWindowRestoreService"/> baseada em Win32.
///
/// Escopo após TASK-013: detecção de processo já em execução, lançamento via
/// <see cref="Process.Start(ProcessStartInfo)"/>, polling até obter um
/// MainWindowHandle válido e, em seguida, reposicionamento + movimento entre
/// virtual desktops + ajuste de WindowState.
///
/// Fluxo:
/// 1. Procurar processo existente cujo <c>MainModule.FileName</c> bata
///    case-insensitive com <see cref="WindowEntry.ExecutablePath"/>; se houver
///    e o título da janela principal contiver/coincidir parcialmente com
///    <see cref="WindowEntry.WindowTitle"/>, considerar achado e usar o
///    <c>MainWindowHandle</c> existente.
/// 2. Caso contrário: validar <see cref="File.Exists"/> e iniciar processo,
///    depois fazer polling em <c>Task.Delay(500ms)</c> até obter um
///    MainWindowHandle válido ou cancelamento/timeout.
/// 3. Reposicionamento (TASK-013):
///    a) Se <see cref="WindowEntry.VirtualDesktopId"/> != null →
///       <see cref="IVirtualDesktopManager.MoveWindowToDesktop"/>. Falha aqui
///       é warning, não bloqueia o resto do fluxo.
///    b) <see cref="Win32Interop.SetWindowPos"/> com SWP_NOZORDER | SWP_NOACTIVATE.
///       ERROR_ACCESS_DENIED (5) → Skipped(AccessDenied); outras falhas →
///       Failed(RepositionFailed).
///    c) <see cref="Win32Interop.ShowWindow"/> conforme <see cref="WindowState"/>:
///       Normal → SW_RESTORE; Maximized → SW_MAXIMIZE; Minimized → SW_MINIMIZE.
/// </summary>
public sealed class Win32WindowRestoreService : IWindowRestoreService
{
    private const int ErrorAccessDenied = 5; // winerror.h ERROR_ACCESS_DENIED
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly IVirtualDesktopManager virtualDesktopManager;

    public Win32WindowRestoreService(IVirtualDesktopManager virtualDesktopManager)
    {
        this.virtualDesktopManager = virtualDesktopManager
            ?? throw new ArgumentNullException(nameof(virtualDesktopManager));
    }

    public async Task<RestoreResult> RestoreWindowAsync(WindowEntry entry, CancellationToken ct)
    {
        if (entry is null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        // Garante timeout interno mesmo quando o caller passa CancellationToken.None
        // (cenários de teste/uso direto). Quando o ct externo já é cancelável,
        // respeitamos exclusivamente ele para não competir com a configuração do caller.
        using var fallbackCts = ct.CanBeCanceled
            ? null
            : new CancellationTokenSource(DefaultTimeout);
        var effectiveCt = fallbackCts?.Token ?? ct;

        // 1. Tentar localizar processo já em execução com janela compatível.
        var existing = TryFindExistingMainWindow(entry);
        if (existing != IntPtr.Zero)
        {
            return ApplyPositioning(entry, existing);
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

        // 3. Polling até MainWindowHandle aparecer ou cancelar/timeout.
        try
        {
            while (true)
            {
                if (effectiveCt.IsCancellationRequested)
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
                    // Apps modernos (Chrome, VS Code, Teams) abrem splash/loading
                    // primeiro e depois trocam pra janela real. Esperar um pouco
                    // e re-obter MainWindowHandle pega o handle final estável,
                    // evitando reposicionar uma janela transitória.
                    try { await Task.Delay(TimeSpan.FromMilliseconds(1500), effectiveCt).ConfigureAwait(false); }
                    catch (OperationCanceledException) { /* segue com handle atual */ }

                    try { launched.Refresh(); } catch { }
                    IntPtr finalHandle;
                    try { finalHandle = launched.MainWindowHandle; } catch { finalHandle = handle; }
                    if (finalHandle == IntPtr.Zero) finalHandle = handle;

                    var result = ApplyPositioning(entry, finalHandle);

                    // Re-aplicar posição depois de mais 1s — alguns apps continuam
                    // ajustando layout interno após primeiro paint e desfazem nosso
                    // SetWindowPos. Aplicar 2x estabiliza posição.
                    try { await Task.Delay(TimeSpan.FromMilliseconds(1000), effectiveCt).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return result; }
                    try { launched.Refresh(); } catch { }
                    IntPtr settledHandle;
                    try { settledHandle = launched.MainWindowHandle; } catch { settledHandle = finalHandle; }
                    if (settledHandle == IntPtr.Zero) settledHandle = finalHandle;
                    ApplyPositioning(entry, settledHandle);

                    return result;
                }

                try
                {
                    await Task.Delay(PollInterval, effectiveCt).ConfigureAwait(false);
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
            // Process.Dispose libera apenas o handle gerenciado; o processo
            // continua vivo e dono da janela.
            launched.Dispose();
        }
    }

    /// <summary>
    /// Aplica os 3 passos de reposicionamento (virtual desktop, geometria,
    /// estado). Cada passo trata seus próprios erros conforme contrato da task.
    /// </summary>
    private RestoreResult ApplyPositioning(WindowEntry entry, IntPtr hwnd)
    {
        // 3.a — Virtual desktop: falha aqui é warning, NÃO interrompe o fluxo.
        if (entry.VirtualDesktopId.HasValue)
        {
            try
            {
                virtualDesktopManager.MoveWindowToDesktop(hwnd, entry.VirtualDesktopId.Value);
                // false → seguimos. O stub sempre retorna false; isso é esperado.
            }
            catch
            {
                // Defensivo: a interface promete não lançar, mas blindamos para
                // que falhas de implementação não derrubem a restauração.
            }
        }

        // 3.b — Geometria.
        const uint flags = Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE;
        var ok = Win32Interop.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            entry.X,
            entry.Y,
            entry.Width,
            entry.Height,
            flags);

        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            if (err == ErrorAccessDenied)
            {
                return RestoreResult.Skipped(
                    RestoreFailureCode.AccessDenied,
                    $"SetWindowPos denied (ERROR_ACCESS_DENIED) for '{entry.ProcessName}'");
            }

            return RestoreResult.Failed(
                RestoreFailureCode.RepositionFailed,
                $"SetWindowPos failed (Win32 error {err}) for '{entry.ProcessName}'");
        }

        // 3.c — Estado da janela (não falhar se ShowWindow retornar false:
        // o valor de retorno indica estado anterior, não erro).
        var cmd = MapWindowStateToShowWindowCmd(entry.WindowState);
        try
        {
            Win32Interop.ShowWindow(hwnd, cmd);
        }
        catch (Win32Exception)
        {
            // Defensivo — ShowWindow raramente lança.
        }

        return RestoreResult.Success("Window restored");
    }

    private static int MapWindowStateToShowWindowCmd(WindowState state) => state switch
    {
        WindowState.Maximized => Win32Interop.SW_MAXIMIZE,
        WindowState.Minimized => Win32Interop.SW_MINIMIZE,
        WindowState.Normal => Win32Interop.SW_RESTORE,
        _ => Win32Interop.SW_RESTORE
    };

    /// <summary>
    /// Enumera <see cref="Process.GetProcesses"/> tentando casar pelo caminho
    /// completo do executável (case-insensitive). Retorna o
    /// <see cref="Process.MainWindowHandle"/> quando encontra match
    /// compatível com <see cref="WindowEntry.WindowTitle"/>; caso contrário,
    /// <see cref="IntPtr.Zero"/>. Garante <see cref="Process.Dispose"/> em
    /// todos os processos enumerados (inclusive o vencedor — só precisamos do
    /// hwnd, que pertence ao SO e permanece válido).
    /// <c>MainModule.FileName</c> pode lançar
    /// <see cref="Win32Exception"/> em processos elevados/protegidos — nestes
    /// casos ignoramos silenciosamente.
    /// </summary>
    private static IntPtr TryFindExistingMainWindow(WindowEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.ExecutablePath))
        {
            return IntPtr.Zero;
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
            return IntPtr.Zero;
        }

        try
        {
            foreach (var p in all)
            {
                using (p)
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
                        continue;
                    }

                    if (!string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string title;
                    IntPtr hwnd;
                    try
                    {
                        title = p.MainWindowTitle ?? string.Empty;
                        hwnd = p.MainWindowHandle;
                    }
                    catch
                    {
                        continue;
                    }

                    if (hwnd == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Matching parcial case-insensitive: título capturado contido no
                    // título atual OU vice-versa (para tolerar pequenas mudanças).
                    // Quando WindowTitle capturado está vazio, basta o path bater.
                    var matches = string.IsNullOrEmpty(targetTitle)
                        || title.Contains(targetTitle, StringComparison.OrdinalIgnoreCase)
                        || targetTitle.Contains(title, StringComparison.OrdinalIgnoreCase);

                    if (matches)
                    {
                        return hwnd;
                    }
                }
            }
        }
        catch
        {
            // Defensivo: enumeração não deve derrubar a restauração.
        }

        return IntPtr.Zero;
    }
}
