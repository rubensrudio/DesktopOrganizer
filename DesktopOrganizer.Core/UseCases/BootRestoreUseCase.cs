using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Core.UseCases;

/// <summary>
/// Use case responsável pela restauração automática no boot.
///
/// Lê <see cref="AppConfig.DefaultBootSnapshotId"/> e, se houver um snapshot
/// configurado e ainda existente, delega a restauração para
/// <see cref="RestoreSnapshotUseCase"/>.
///
/// Casos sem-op (retorna sem lançar):
/// - Nenhum snapshot de boot configurado (<c>DefaultBootSnapshotId == null</c>);
/// - Snapshot configurado foi deletado (repositório retorna <c>null</c>).
///
/// NOTA: a espera por "desktop pronto" (eventos de sessão/shell via
/// SystemEvents) é responsabilidade do bootstrap (TASK-018). Este use case
/// contém APENAS a lógica de decisão e delegação.
/// </summary>
public sealed class BootRestoreUseCase
{
    private readonly IConfigRepository _configRepository;
    private readonly ISnapshotRepository _snapshotRepository;
    private readonly RestoreSnapshotUseCase _restoreUseCase;

    public BootRestoreUseCase(
        IConfigRepository configRepository,
        ISnapshotRepository snapshotRepository,
        RestoreSnapshotUseCase restoreUseCase)
    {
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
        _snapshotRepository = snapshotRepository ?? throw new ArgumentNullException(nameof(snapshotRepository));
        _restoreUseCase = restoreUseCase ?? throw new ArgumentNullException(nameof(restoreUseCase));
    }

    /// <summary>
    /// Executa a restauração de boot, se configurada. Não lança quando o
    /// snapshot de boot não está configurado ou foi deletado.
    /// </summary>
    public async Task ExecuteAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var config = await _configRepository.LoadAsync().ConfigureAwait(false);

        if (config.DefaultBootSnapshotId is null)
        {
            return;
        }

        var snapshot = await _snapshotRepository
            .GetByIdAsync(config.DefaultBootSnapshotId.Value)
            .ConfigureAwait(false);

        if (snapshot is null)
        {
            return;
        }

        await _restoreUseCase.ExecuteAsync(snapshot, ct).ConfigureAwait(false);
    }
}
