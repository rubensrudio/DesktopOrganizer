using DesktopOrganizer.Core.Domain;

namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Persistência de perfis. A deleção é em cascata (remove os snapshots do
/// perfil) — responsabilidade da implementação.
/// </summary>
public interface IProfileRepository
{
    Task SaveAsync(Profile profile);
    Task<IReadOnlyList<Profile>> GetAllAsync();
    Task DeleteAsync(Guid id);
}
