using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Core.UseCases;

/// <summary>
/// Use case responsável por gerenciar perfis (CRUD básico + ativação).
///
/// Observação sobre delete: a cascata (apagar snapshots associados) é
/// responsabilidade do <see cref="IProfileRepository"/>. Este use case
/// apenas dispara o delete; tratar a transição do perfil ativo após delete
/// é responsabilidade do bootstrap/UI.
/// </summary>
public sealed class ManageProfilesUseCase
{
    private readonly IProfileRepository _profileRepository;
    private readonly IConfigRepository _configRepository;

    public ManageProfilesUseCase(
        IProfileRepository profileRepository,
        IConfigRepository configRepository)
    {
        _profileRepository = profileRepository ?? throw new ArgumentNullException(nameof(profileRepository));
        _configRepository = configRepository ?? throw new ArgumentNullException(nameof(configRepository));
    }

    /// <summary>
    /// Retorna todos os perfis cadastrados.
    /// </summary>
    public Task<IReadOnlyList<Profile>> GetAllProfilesAsync()
    {
        return _profileRepository.GetAllAsync();
    }

    /// <summary>
    /// Cria um novo perfil com Id gerado e CreatedAt = UTC now, persiste e
    /// retorna a instância criada.
    /// </summary>
    public async Task<Profile> CreateProfileAsync(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        var profile = new Profile
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _profileRepository.SaveAsync(profile).ConfigureAwait(false);

        return profile;
    }

    /// <summary>
    /// Define o perfil informado como ativo na configuração persistida.
    /// </summary>
    public async Task ActivateProfileAsync(Guid id)
    {
        var config = await _configRepository.LoadAsync().ConfigureAwait(false);
        config.ActiveProfileId = id;
        await _configRepository.SaveAsync(config).ConfigureAwait(false);
    }

    /// <summary>
    /// Remove o perfil pelo Id. A cascata em snapshots é responsabilidade do
    /// repositório. A transição do perfil ativo deve ser tratada pelo
    /// chamador (bootstrap/UI) caso o perfil deletado seja o ativo.
    /// </summary>
    public Task DeleteProfileAsync(Guid id)
    {
        return _profileRepository.DeleteAsync(id);
    }
}
