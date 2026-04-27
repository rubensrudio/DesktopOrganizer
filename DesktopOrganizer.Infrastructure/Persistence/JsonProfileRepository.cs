using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="IProfileRepository"/> que persiste perfis como
/// arquivos JSON em
/// <c>%APPDATA%\DesktopOrganizer\profiles\&lt;profileId&gt;\profile.json</c>.
///
/// Garantias:
/// - Cria diretórios automaticamente quando necessário.
/// - <see cref="DeleteAsync(Guid)"/> apaga o diretório inteiro do perfil
///   (cascata sobre <c>snapshots\</c>).
/// - Arquivos <c>profile.json</c> corrompidos ou ilegíveis são ignorados em
///   <see cref="GetAllAsync"/> para não abortar a enumeração.
/// </summary>
public class JsonProfileRepository : IProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDataPaths _paths;

    public JsonProfileRepository(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task SaveAsync(Profile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        if (profile.Id == Guid.Empty)
        {
            throw new ArgumentException(
                "Profile.Id não pode ser Guid.Empty.",
                nameof(profile));
        }

        _paths.EnsureProfileDirectory(profile.Id);
        var filePath = _paths.GetProfileFilePath(profile.Id);

        // Escreve via stream assíncrono — System.Text.Json suporta nativamente.
        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await JsonSerializer.SerializeAsync(stream, profile, SerializerOptions)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Profile>> GetAllAsync()
    {
        var profilesRoot = _paths.ProfilesRoot;
        if (!Directory.Exists(profilesRoot))
        {
            return Array.Empty<Profile>();
        }

        var result = new List<Profile>();

        foreach (var profileDir in Directory.EnumerateDirectories(profilesRoot))
        {
            var filePath = Path.Combine(profileDir, "profile.json");
            if (!File.Exists(filePath))
            {
                // Diretório sem profile.json: ignora silenciosamente.
                continue;
            }

            Profile? profile = null;
            try
            {
                await using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);

                profile = await JsonSerializer
                    .DeserializeAsync<Profile>(stream, SerializerOptions)
                    .ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // Arquivo corrompido — ignora a entrada para não quebrar a
                // enumeração dos demais perfis.
                continue;
            }
            catch (IOException)
            {
                // Arquivo bloqueado/ilegível — mesma estratégia.
                continue;
            }

            if (profile is not null)
            {
                result.Add(profile);
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

        var profileDir = _paths.GetProfileDirectory(id);
        if (Directory.Exists(profileDir))
        {
            // Cascata: apaga o diretório inteiro do perfil, incluindo
            // subdiretório snapshots\ e todos os snapshots dentro dele.
            Directory.Delete(profileDir, recursive: true);
        }

        return Task.CompletedTask;
    }
}
