using DesktopOrganizer.Core.Domain;

namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Persistência de snapshots. Implementação concreta em Infrastructure
/// (JsonSnapshotRepository).
/// </summary>
public interface ISnapshotRepository
{
    Task SaveAsync(Snapshot snapshot);
    Task<Snapshot?> GetByIdAsync(Guid id);
    Task<IReadOnlyList<Snapshot>> GetByProfileIdAsync(Guid profileId);
    Task DeleteAsync(Guid id);
}
