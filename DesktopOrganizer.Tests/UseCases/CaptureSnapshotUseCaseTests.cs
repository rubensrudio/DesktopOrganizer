using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using NSubstitute;
using Xunit;

namespace DesktopOrganizer.Tests.UseCases;

/// <summary>
/// Testes unitários do <see cref="CaptureSnapshotUseCase"/>.
/// Cobertura: edge case de lista vazia (DORG AC5) e fluxo feliz com 2 janelas.
/// </summary>
public class CaptureSnapshotUseCaseTests
{
    private readonly IWindowCaptureService _captureService = Substitute.For<IWindowCaptureService>();
    private readonly ISnapshotRepository _snapshotRepository = Substitute.For<ISnapshotRepository>();
    private readonly IConfigRepository _configRepository = Substitute.For<IConfigRepository>();
    private readonly ITrayNotificationService _notificationService = Substitute.For<ITrayNotificationService>();

    private CaptureSnapshotUseCase CreateSut() =>
        new(_captureService, _snapshotRepository, _configRepository, _notificationService);

    [Fact]
    public async Task ExecuteAsync_QuandoListaDeJanelasVazia_NaoSalvaSnapshotEAvisaUsuario()
    {
        // Arrange
        _captureService
            .CaptureAllWindowsAsync()
            .Returns(Task.FromResult<IReadOnlyList<WindowEntry>>(new List<WindowEntry>()));

        var sut = CreateSut();

        // Act
        var result = await sut.ExecuteAsync("snapshot-vazio");

        // Assert
        Assert.Null(result);
        await _snapshotRepository.DidNotReceive().SaveAsync(Arg.Any<Snapshot>());
        _notificationService.Received(1).ShowSuccess(Arg.Is<string>(m => m.Contains("Nenhuma janela")));
        await _configRepository.DidNotReceive().LoadAsync();
    }

    [Fact]
    public async Task ExecuteAsync_QuandoDuasJanelasCapturadas_SalvaSnapshotENotifica()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var windows = new List<WindowEntry>
        {
            new() { ProcessName = "notepad", ExecutablePath = @"C:\Windows\notepad.exe" },
            new() { ProcessName = "Code", ExecutablePath = @"C:\Code\Code.exe" }
        };

        _captureService
            .CaptureAllWindowsAsync()
            .Returns(Task.FromResult<IReadOnlyList<WindowEntry>>(windows));

        _configRepository
            .LoadAsync()
            .Returns(Task.FromResult(new AppConfig { ActiveProfileId = profileId }));

        Snapshot? captured = null;
        await _snapshotRepository.SaveAsync(Arg.Do<Snapshot>(s => captured = s));

        var sut = CreateSut();

        // Act
        var result = await sut.ExecuteAsync("Dev Setup");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Dev Setup", result!.Name);
        Assert.Equal(profileId, result.ProfileId);
        Assert.Equal(2, result.Windows.Count);

        await _snapshotRepository.Received(1).SaveAsync(Arg.Any<Snapshot>());
        Assert.NotNull(captured);
        Assert.Equal(2, captured!.Windows.Count);

        _notificationService.Received(1).ShowSuccess(Arg.Is<string>(m => m.Contains("Dev Setup")));
    }
}
