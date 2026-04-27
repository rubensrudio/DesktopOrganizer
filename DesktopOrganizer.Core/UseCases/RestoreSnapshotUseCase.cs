using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Core.UseCases;

/// <summary>
/// Use case responsável por restaurar todas as janelas de um <see cref="Snapshot"/>.
///
/// Concorrência: limita a 3 restaurações simultâneas via <see cref="SemaphoreSlim"/>
/// para evitar saturar o desktop manager / spawnar muitos processos ao mesmo
/// tempo. Cada restauração individual recebe um timeout (configurável via
/// <see cref="AppConfig.RestoreTimeoutSeconds"/>) através de um
/// <see cref="CancellationTokenSource"/> linkado ao <c>ct</c> do caller.
///
/// Política de erros: exceções de <see cref="IWindowRestoreService.RestoreWindowAsync"/>
/// (incluindo <see cref="OperationCanceledException"/> por timeout) são
/// capturadas e contabilizadas como Failed — NÃO propagam, para que uma
/// janela problemática não aborte a restauração das demais. Cancelamento
/// vindo do caller (<c>ct</c>) propaga normalmente.
///
/// Contagem final: <c>restored</c> = janelas com Status=Success;
/// <c>failed</c> = janelas com Status=Failed. Skipped (ex.: executável
/// inexistente) é deliberado e NÃO conta como falha.
/// </summary>
public sealed class RestoreSnapshotUseCase
{
    private const int MaxConcurrentRestores = 3;

    private readonly IWindowRestoreService _restoreService;
    private readonly ITrayNotificationService _notificationService;
    private readonly IConfigRepository _configRepository;

    public RestoreSnapshotUseCase(
        IWindowRestoreService restoreService,
        ITrayNotificationService notificationService,
        IConfigRepository configRepository)
    {
        _restoreService = restoreService ?? throw new ArgumentNullException(nameof(restoreService));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
    }

    /// <summary>
    /// Restaura todas as janelas do snapshot fornecido com concorrência
    /// limitada e timeout por janela. Notifica o usuário ao final com a
    /// contagem de sucessos vs. falhas.
    /// </summary>
    public async Task ExecuteAsync(Snapshot snapshot, CancellationToken ct = default)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        ct.ThrowIfCancellationRequested();

        var config = await _configRepository.LoadAsync().ConfigureAwait(false);
        var timeout = TimeSpan.FromSeconds(config.RestoreTimeoutSeconds);

        var windows = snapshot.Windows ?? new List<WindowEntry>();

        if (windows.Count == 0)
        {
            _notificationService.ShowResult(restored: 0, failed: 0);
            return;
        }

        using var semaphore = new SemaphoreSlim(MaxConcurrentRestores, MaxConcurrentRestores);

        var tasks = new List<Task<RestoreResult>>(windows.Count);

        foreach (var entry in windows)
        {
            tasks.Add(RestoreOneAsync(entry, semaphore, timeout, ct));
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        var restored = 0;
        var failed = 0;
        foreach (var result in results)
        {
            if (result.Status == RestoreStatus.Success)
            {
                restored++;
            }
            else if (result.Status == RestoreStatus.Failed)
            {
                failed++;
            }
            // Skipped é deliberado — não conta como falha.
        }

        _notificationService.ShowResult(restored, failed);
    }

    private async Task<RestoreResult> RestoreOneAsync(
        WindowEntry entry,
        SemaphoreSlim semaphore,
        TimeSpan timeout,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linkedCts.CancelAfter(timeout);

            try
            {
                return await _restoreService.RestoreWindowAsync(entry, linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Cancelamento do caller — propaga.
                throw;
            }
            catch (OperationCanceledException)
            {
                // Timeout individual da janela — contabiliza como Failed.
                return RestoreResult.Failed(
                    RestoreFailureCode.LaunchFailed,
                    $"Timeout restaurando janela após {timeout.TotalSeconds:F0}s.");
            }
            catch (Exception ex)
            {
                // Qualquer outra exceção da implementação do restoreService —
                // não deixa uma janela problemática derrubar as demais.
                return RestoreResult.Failed(
                    RestoreFailureCode.LaunchFailed,
                    $"Falha inesperada: {ex.Message}");
            }
        }
        finally
        {
            semaphore.Release();
        }
    }
}
