using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Core.UseCases;

/// <summary>
/// Use case responsável por capturar o estado atual das janelas e persistir
/// como um <see cref="Snapshot"/> associado ao perfil ativo.
///
/// Edge case (DORG AC5): se nenhuma janela relevante for capturada, o use
/// case NÃO cria snapshot vazio — apenas notifica o usuário e retorna
/// <c>null</c>.
/// </summary>
public sealed class CaptureSnapshotUseCase
{
    private readonly IWindowCaptureService _captureService;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly IConfigRepository _configRepository;
    private readonly ITrayNotificationService _notificationService;

    public CaptureSnapshotUseCase(
        IWindowCaptureService captureService,
        ISnapshotRepository snapshotRepository,
        IConfigRepository configRepository,
        ITrayNotificationService notificationService)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    /// <summary>
    /// Captura as janelas atuais, persiste como snapshot vinculado ao perfil
    /// ativo e notifica o usuário. Retorna <c>null</c> se nenhuma janela
    /// relevante for capturada.
    /// </summary>
    /// <param name="snapshotName">Nome legível do snapshot (fornecido pelo usuário).</param>
    /// <param name="ct">Token de cancelamento (opcional).</param>
    public async Task<Snapshot?> ExecuteAsync(string snapshotName, CancellationToken ct = default)
    {
        if (snapshotName is null)
        {
            throw new ArgumentNullException(nameof(snapshotName));
        }

        ct.ThrowIfCancellationRequested();

        var capture = await _captureService.CaptureAllWindowsAsync().ConfigureAwait(false);

        if (capture is null || capture.Count == 0)
        {
            _notificationService.ShowSuccess("Nenhuma janela aberta para capturar");
            return null;
        }

        ct.ThrowIfCancellationRequested();

        var config = await _configRepository.LoadAsync().ConfigureAwait(false);

        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = snapshotName,
            ProfileId = config.ActiveProfileId,
            CapturedAt = DateTimeOffset.UtcNow,
            Windows = capture.ToList()
        };

        await _snapshotRepository.SaveAsync(snapshot).ConfigureAwait(false);

        _notificationService.ShowSuccess($"Snapshot salvo: {snapshotName}");

        return snapshot;
    }
}
