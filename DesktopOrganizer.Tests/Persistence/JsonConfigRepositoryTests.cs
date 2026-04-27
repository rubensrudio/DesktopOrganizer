using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Infrastructure.Persistence;
using Xunit;

namespace DesktopOrganizer.Tests.Persistence;

/// <summary>
/// Testes de integração do <see cref="JsonConfigRepository"/> em diretório
/// temporário isolado por instância de teste.
/// </summary>
public class JsonConfigRepositoryTests : IDisposable
{
    private readonly string _basePath;
    private readonly AppDataPaths _paths;
    private readonly JsonConfigRepository _sut;

    public JsonConfigRepositoryTests()
    {
        _basePath = Path.Combine(
            Path.GetTempPath(),
            "DesktopOrganizerTests-" + Guid.NewGuid());
        Directory.CreateDirectory(_basePath);

        _paths = new AppDataPaths(_basePath);
        _sut = new JsonConfigRepository(_paths);
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
            // Cleanup best-effort.
        }
    }

    [Fact]
    public async Task LoadAsync_QuandoArquivoNaoExiste_RetornaAppConfigComDefaults()
    {
        // Arrange — diretório recém-criado, sem config.json.

        // Act
        var config = await _sut.LoadAsync();

        // Assert
        Assert.NotNull(config);
        Assert.Equal(30, config.RestoreTimeoutSeconds);
        Assert.Equal(Guid.Empty, config.ActiveProfileId);
        Assert.Null(config.DefaultBootSnapshotId);
        Assert.False(config.StartupEnabled);

        // Garantir que LoadAsync não criou o arquivo (sem side effects).
        Assert.False(File.Exists(_paths.GetConfigFilePath()));
    }

    [Fact]
    public async Task SaveAsyncSeguidoDeLoadAsync_RoundTripPreservaCamposModificados()
    {
        // Arrange
        var original = new AppConfig
        {
            ActiveProfileId = Guid.NewGuid(),
            DefaultBootSnapshotId = Guid.NewGuid(),
            StartupEnabled = true,
            RestoreTimeoutSeconds = 75
        };

        // Act
        await _sut.SaveAsync(original);
        var loaded = await _sut.LoadAsync();

        // Assert
        Assert.Equal(original.ActiveProfileId, loaded.ActiveProfileId);
        Assert.Equal(original.DefaultBootSnapshotId, loaded.DefaultBootSnapshotId);
        Assert.Equal(original.StartupEnabled, loaded.StartupEnabled);
        Assert.Equal(75, loaded.RestoreTimeoutSeconds);
        Assert.True(File.Exists(_paths.GetConfigFilePath()));
    }
}
