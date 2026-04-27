using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using NSubstitute;
using Xunit;

namespace DesktopOrganizer.Tests.UseCases;

/// <summary>
/// Testes unitários do <see cref="RestoreSnapshotUseCase"/>.
/// Cobertura: snapshot com mix de Success/Failed (não propaga exceção) e
/// snapshot vazio (notifica 0,0).
/// </summary>
public class RestoreSnapshotUseCaseTests
{
    private readonly IWindowRestoreService _restoreService = Substitute.For<IWindowRestoreService>();
    private readonly ITrayNotificationService _notificationService = Substitute.For<ITrayNotificationService>();
    private readonly IConfigRepository _configRepository = Substitute.For<IConfigRepository>();

    public RestoreSnapshotUseCaseTests()
    {
        _configRepository
            .LoadAsync()
            .Returns(Task.FromResult(new AppConfig { RestoreTimeoutSeconds = 30 }));
    }

    private RestoreSnapshotUseCase CreateSut() =>
        new(_restoreService, _notificationService, _configRepository);

    [Fact]
    public async Task ExecuteAsync_TresJanelasComUmFalha_NotificaContagemSemPropagar()
    {
        // Arrange
        var w1 = new WindowEntry { ProcessName = "a" };
        var w2 = new WindowEntry { ProcessName = "b" };
        var w3 = new WindowEntry { ProcessName = "c" };

        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "test",
            Windows = new List<WindowEntry> { w1, w2, w3 }
        };

        _restoreService
            .RestoreWindowAsync(w1, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RestoreResult.Failed(RestoreFailureCode.LaunchFailed, "boom")));

        _restoreService
            .RestoreWindowAsync(w2, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RestoreResult.Success()));

        _restoreService
            .RestoreWindowAsync(w3, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(RestoreResult.Success()));

        var sut = CreateSut();

        // Act
        var ex = await Record.ExceptionAsync(() => sut.ExecuteAsync(snapshot));

        // Assert
        Assert.Null(ex);
        _notificationService.Received(1).ShowResult(2, 1);
    }

    [Fact]
    public async Task ExecuteAsync_SnapshotVazio_NotificaZeroZero()
    {
        // Arrange
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "vazio",
            Windows = new List<WindowEntry>()
        };

        var sut = CreateSut();

        // Act
        await sut.ExecuteAsync(snapshot);

        // Assert
        _notificationService.Received(1).ShowResult(0, 0);
        await _restoreService
            .DidNotReceive()
            .RestoreWindowAsync(Arg.Any<WindowEntry>(), Arg.Any<CancellationToken>());
    }
}
