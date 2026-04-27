using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;
using DesktopOrganizer.Core.UseCases;
using NSubstitute;
using Xunit;

namespace DesktopOrganizer.Tests.UseCases;

/// <summary>
/// Testes unitários do <see cref="ManageProfilesUseCase"/> cobrindo o fluxo de
/// criação, ativação e deleção de perfis.
/// </summary>
public class ManageProfilesUseCaseTests
{
    private readonly IProfileRepository _profileRepository = Substitute.For<IProfileRepository>();
    private readonly IConfigRepository _configRepository = Substitute.For<IConfigRepository>();

    private ManageProfilesUseCase CreateSut() =>
        new(_profileRepository, _configRepository);

    [Fact]
    public async Task CreateProfileAsync_PersisteProfileComNomeCorretoEIdNaoVazio()
    {
        // Arrange
        Profile? captured = null;
        await _profileRepository.SaveAsync(Arg.Do<Profile>(p => captured = p));
        var sut = CreateSut();

        // Act
        var result = await sut.CreateProfileAsync("Trabalho");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Trabalho", result.Name);
        Assert.NotEqual(Guid.Empty, result.Id);

        await _profileRepository.Received(1).SaveAsync(Arg.Any<Profile>());
        Assert.NotNull(captured);
        Assert.Equal("Trabalho", captured!.Name);
        Assert.NotEqual(Guid.Empty, captured.Id);
    }

    [Fact]
    public async Task ActivateProfileAsync_AtualizaActiveProfileIdNoConfigEPersistir()
    {
        // Arrange
        var existing = new AppConfig
        {
            ActiveProfileId = Guid.NewGuid(),
            RestoreTimeoutSeconds = 45
        };
        _configRepository.LoadAsync().Returns(Task.FromResult(existing));

        AppConfig? saved = null;
        await _configRepository.SaveAsync(Arg.Do<AppConfig>(c => saved = c));

        var newProfileId = Guid.NewGuid();
        var sut = CreateSut();

        // Act
        await sut.ActivateProfileAsync(newProfileId);

        // Assert
        await _configRepository.Received(1).LoadAsync();
        await _configRepository.Received(1).SaveAsync(Arg.Any<AppConfig>());
        Assert.NotNull(saved);
        Assert.Equal(newProfileId, saved!.ActiveProfileId);
        // Garante que demais campos do AppConfig são preservados (não resetados).
        Assert.Equal(45, saved.RestoreTimeoutSeconds);
    }

    [Fact]
    public async Task DeleteProfileAsync_DelegaParaProfileRepositoryComIdCorreto()
    {
        // Arrange
        var id = Guid.NewGuid();
        var sut = CreateSut();

        // Act
        await sut.DeleteProfileAsync(id);

        // Assert
        await _profileRepository.Received(1).DeleteAsync(id);
    }
}
