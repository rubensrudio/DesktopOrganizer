using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using NSubstitute;
using Xunit;

namespace DesktopOrganizer.Tests.UseCases;

/// <summary>
/// Testes unitários do <see cref="BootRestoreUseCase"/>.
///
/// Observação: <see cref="RestoreSnapshotUseCase"/> é uma classe selada sem
/// interface, então não dá para mockar diretamente com NSubstitute. Validamos
/// "restore não foi invocado" verificando que o pipeline anterior
/// (<see cref="ISnapshotRepository.GetByIdAsync"/>) não foi chamado, o que
/// é o gate imediato antes da delegação para o restore use case.
/// </summary>
public class BootRestoreUseCaseTests
{
    private readonly IConfigRepository _configRepository = Substitute.For<IConfigRepository>();
    private readonly ISnapshotRepository _snapshotRepository = Substitute.For<ISnapshotRepository>();

    // Dependências usadas para construir o RestoreSnapshotUseCase concreto
    // (necessário porque é uma classe selada sem interface).
    private readonly IWindowRestoreService _restoreService = Substitute.For<IWindowRestoreService>();
    private readonly ITrayNotificationService _notificationService = Substitute.For<ITrayNotificationService>();

    private BootRestoreUseCase CreateSut()
    {
        var restoreUseCase = new RestoreSnapshotUseCase(
            _restoreService,
            _notificationService,
            _configRepository);

        return new BootRestoreUseCase(_configRepository, _snapshotRepository, restoreUseCase);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoDefaultBootSnapshotIdEhNull_NaoBuscaSnapshotNemRestaura()
    {
        // Arrange
        _configRepository
            .LoadAsync()
            .Returns(Task.FromResult(new AppConfig { DefaultBootSnapshotId = null }));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert
        await _snapshotRepository
            .DidNotReceive()
            .GetByIdAsync(Arg.Any<Guid>());

        await _restoreService
            .DidNotReceive()
            .RestoreWindowAsync(Arg.Any<WindowEntry>(), Arg.Any<CancellationToken>());

        _notificationService.DidNotReceive().ShowResult(Arg.Any<int>(), Arg.Any<int>());
    }

    [Fact]
    public async Task ExecuteAsync_QuandoIdValidoESnapshotExiste_BuscaPorIdEDelegaRestore()
    {
        // Arrange
        var bootId = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Id = bootId,
            Name = "boot",
            Windows = new List<WindowEntry>() // vazio para evitar invocar RestoreService
        };

        _configRepository
            .LoadAsync()
            .Returns(Task.FromResult(new AppConfig
            {
                DefaultBootSnapshotId = bootId,
                RestoreTimeoutSeconds = 30
            }));

        _snapshotRepository
            .GetByIdAsync(bootId)
            .Returns(Task.FromResult<Snapshot?>(snapshot));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert
        await _snapshotRepository.Received(1).GetByIdAsync(bootId);
        // Como snapshot estava vazio, RestoreSnapshotUseCase notificou (0,0):
        // prova que a delegação aconteceu.
        _notificationService.Received(1).ShowResult(0, 0);
    }

    [Fact]
    public async Task ExecuteAsync_QuandoIdValidoESnapshotNull_NaoChamaRestore()
    {
        // Arrange
        var bootId = Guid.NewGuid();

        _configRepository
            .LoadAsync()
            .Returns(Task.FromResult(new AppConfig { DefaultBootSnapshotId = bootId }));

        _snapshotRepository
            .GetByIdAsync(bootId)
            .Returns(Task.FromResult<Snapshot?>(null));

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync();

        // Assert
        await _snapshotRepository.Received(1).GetByIdAsync(bootId);
        await _restoreService
            .DidNotReceive()
            .RestoreWindowAsync(Arg.Any<WindowEntry>(), Arg.Any<CancellationToken>());
        _notificationService.DidNotReceive().ShowResult(Arg.Any<int>(), Arg.Any<int>());
    }
}
