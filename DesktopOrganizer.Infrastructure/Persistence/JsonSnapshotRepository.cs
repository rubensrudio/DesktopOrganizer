using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="ISnapshotRepository"/> que persiste snapshots
/// como arquivos JSON em
/// <c>%APPDATA%\DesktopOrganizer\profiles\&lt;profileId&gt;\snapshots\snapshot-&lt;snapshotId&gt;.json</c>.
///
/// Garantias:
/// - Cria diretórios automaticamente quando necessário.
/// - Arquivos JSON corrompidos ou ilegíveis são ignorados em
///   <see cref="GetByProfileIdAsync(Guid)"/> e <see cref="GetByIdAsync(Guid)"/>
///   para não abortar a enumeração dos demais snapshots.
/// - <see cref="DeleteAsync(Guid)"/> recebe apenas o id do snapshot; a
///   localização do arquivo é determinada varrendo todos os perfis até
///   encontrar o primeiro <c>snapshot-&lt;id&gt;.json</c> correspondente.
/// </summary>
public class JsonSnapshotRepository : ISnapshotRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDataPaths _paths;

    public JsonSnapshotRepository(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task SaveAsync(Snapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (snapshot.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "Snapshot.Id não pode ser Guid.Empty.",
                nameof(snapshot));
        }

        if (snapshot.ProfileId == Guid.Empty)
        {
            throw new ArgumentException(
                "Snapshot.ProfileId não pode ser Guid.Empty.",
                nameof(snapshot));
        }

        _paths.EnsureSnapshotsDirectory(snapshot.ProfileId);
        var filePath = _paths.GetSnapshotFilePath(snapshot.ProfileId, snapshot.Id);

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions)
            .ConfigureAwait(false);
    }

    public async Task<Snapshot?> GetByIdAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Id não pode ser Guid.Empty.",
                nameof(id));
        }

        var profilesRoot = _paths.ProfilesRoot;
        if (!Directory.Exists(profilesRoot))
        {
            return null;
        }

        foreach (var profileDir in Directory.EnumerateDirectories(profilesRoot))
        {
            var snapshotsDir = Path.Combine(profileDir, "snapshots");
            if (!Directory.Exists(snapshotsDir))
            {
                continue;
            }

            var candidate = Path.Combine(
                snapshotsDir,
                "snapshot-" + id + ".json");

            if (!File.Exists(candidate))
            {
                continue;
            }

            var snapshot = await TryReadSnapshotAsync(candidate).ConfigureAwait(false);
            if (snapshot is not null)
            {
                return snapshot;
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<Snapshot>> GetByProfileIdAsync(Guid profileId)
    {
        if (profileId == Guid.Empty)
        {
            throw new ArgumentException(
                "ProfileId não pode ser Guid.Empty.",
                nameof(profileId));
        }

        var snapshotsDir = _paths.GetSnapshotsDirectory(profileId);
        if (!Directory.Exists(snapshotsDir))
        {
            return Array.Empty<Snapshot>();
        }

        var result = new List<Snapshot>();

        foreach (var filePath in Directory.EnumerateFiles(
                     snapshotsDir,
                     AppDataPaths.SnapshotSearchPattern))
        {
            var snapshot = await TryReadSnapshotAsync(filePath).ConfigureAwait(false);
            if (snapshot is not null)
            {
                result.Add(snapshot);
            }
        }

        return result;
    }

    public Task DeleteAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException(
                "Id não pode ser Guid.Empty.",
                nameof(id));
        }

        var profilesRoot = _paths.ProfilesRoot;
        if (!Directory.Exists(profilesRoot))
        {
            return Task.CompletedTask;
        }

        // Estratégia: enumerar diretórios de perfis e apagar o primeiro
        // arquivo snapshot-<id>.json encontrado. A interface não expõe o
        // profileId no DeleteAsync, portanto a varredura é necessária.
        foreach (var profileDir in Directory.EnumerateDirectories(profilesRoot))
        {
            var snapshotsDir = Path.Combine(profileDir, "snapshots");
            if (!Directory.Exists(snapshotsDir))
            {
                continue;
            }

            var candidate = Path.Combine(
                snapshotsDir,
                "snapshot-" + id + ".json");

            if (File.Exists(candidate))
            {
                File.Delete(candidate);
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }

    private static async Task<Snapshot?> TryReadSnapshotAsync(string filePath)
    {
        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            return await JsonSerializer
                .DeserializeAsync<Snapshot>(stream, SerializerOptions)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Arquivo corrompido — ignora a entrada para não abortar a
            // enumeração dos demais snapshots.
            return null;
        }
        catch (IOException)
        {
            // Arquivo bloqueado/ilegível — mesma estratégia.
            return null;
        }
    }
}
