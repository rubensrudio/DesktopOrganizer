using System.Text;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Infrastructure.Persistence;
using Xunit;

namespace DesktopOrganizer.Tests.Persistence;

/// <summary>
/// Testes de integração do <see cref="JsonSnapshotRepository"/> exercitando o
/// round-trip de serialização contra um diretório temporário isolado por teste.
/// O cleanup é feito via <see cref="IDisposable"/> implementado pela classe.
/// </summary>
public class JsonSnapshotRepositoryTests : IDisposable
{
    private readonly string _basePath;
    private readonly AppDataPaths _paths;
    private readonly JsonSnapshotRepository _sut;

    public JsonSnapshotRepositoryTests()
    {
        _basePath = Path.Combine(
            Path.GetTempPath(),
            "DesktopOrganizerTests-" + Guid.NewGuid());
        Directory.CreateDirectory(_basePath);

        _paths = new AppDataPaths(_basePath);
        _sut = new JsonSnapshotRepository(_paths);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }
        catch (IOException)
        {
            // Cleanup best-effort: arquivo pode estar bloqueado por antivírus
            // em CI; não falha o teste por isso.
        }
    }

    [Fact]
    public async Task SaveAsync_ComSnapshotValido_PermiteRoundTripViaGetByIdAsync()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "Dev Setup",
            ProfileId = profileId,
            CapturedAt = new DateTimeOffset(2026, 4, 27, 9, 0, 0, TimeSpan.Zero),
            Windows =
            {
                new WindowEntry
                {
                    Id = Guid.NewGuid(),
                    ProcessName = "Code",
                    ExecutablePath = @"C:\Code\Code.exe",
                    WindowTitle = "projeto - VS Code",
                    X = 10, Y = 20, Width = 1920, Height = 1080,
                    WindowState = WindowState.Maximized,
                    MonitorDeviceName = @"\\.\DISPLAY1",
                    VirtualDesktopId = Guid.NewGuid()
                }
            }
        };

        // Act
        await _sut.SaveAsync(snapshot);
        var loaded = await _sut.GetByIdAsync(snapshot.Id);

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal(snapshot.Id, loaded!.Id);
        Assert.Equal(snapshot.Name, loaded.Name);
        Assert.Equal(snapshot.ProfileId, loaded.ProfileId);
        Assert.Equal(snapshot.CapturedAt, loaded.CapturedAt);
        Assert.Single(loaded.Windows);

        var original = snapshot.Windows[0];
        var roundtripped = loaded.Windows[0];
        Assert.Equal(original.Id, roundtripped.Id);
        Assert.Equal(original.ProcessName, roundtripped.ProcessName);
        Assert.Equal(original.ExecutablePath, roundtripped.ExecutablePath);
        Assert.Equal(original.WindowTitle, roundtripped.WindowTitle);
        Assert.Equal(original.X, roundtripped.X);
        Assert.Equal(original.Y, roundtripped.Y);
        Assert.Equal(original.Width, roundtripped.Width);
        Assert.Equal(original.Height, roundtripped.Height);
        Assert.Equal(original.WindowState, roundtripped.WindowState);
        Assert.Equal(original.MonitorDeviceName, roundtripped.MonitorDeviceName);
        Assert.Equal(original.VirtualDesktopId, roundtripped.VirtualDesktopId);
    }

    [Fact]
    public async Task GetByProfileIdAsync_RetornaSnapshotsPersistidosNoPerfil()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "S1",
            ProfileId = profileId
        };

        // Act
        await _sut.SaveAsync(snapshot);
        var snapshots = await _sut.GetByProfileIdAsync(profileId);

        // Assert
        Assert.Single(snapshots);
        Assert.Equal(snapshot.Id, snapshots[0].Id);
        Assert.Equal("S1", snapshots[0].Name);
    }

    [Fact]
    public async Task DeleteAsync_RemoveOArquivoCorrespondente()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var snapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "ToDelete",
            ProfileId = profileId
        };
        await _sut.SaveAsync(snapshot);
        var filePath = _paths.GetSnapshotFilePath(profileId, snapshot.Id);
        Assert.True(File.Exists(filePath), "pré-condição: arquivo deve existir após SaveAsync");

        // Act
        await _sut.DeleteAsync(snapshot.Id);

        // Assert
        Assert.False(File.Exists(filePath), "arquivo deveria ter sido removido");
        var remaining = await _sut.GetByProfileIdAsync(profileId);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task GetByProfileIdAsync_QuandoExisteArquivoJsonInvalido_IgnoraEContinua()
    {
        // Arrange
        var profileId = Guid.NewGuid();
        var validSnapshot = new Snapshot
        {
            Id = Guid.NewGuid(),
            Name = "Válido",
            ProfileId = profileId
        };
        await _sut.SaveAsync(validSnapshot);

        // Cria um arquivo de snapshot corrompido no mesmo diretório.
        var snapshotsDir = _paths.GetSnapshotsDirectory(profileId);
        var corruptedPath = Path.Combine(
            snapshotsDir,
            "snapshot-" + Guid.NewGuid() + ".json");
        await File.WriteAllTextAsync(
            corruptedPath,
            "{ isto não é JSON válido :::: ",
            Encoding.UTF8);

        // Act
        var snapshots = await _sut.GetByProfileIdAsync(profileId);

        // Assert
        Assert.Single(snapshots);
        Assert.Equal(validSnapshot.Id, snapshots[0].Id);
    }
}
