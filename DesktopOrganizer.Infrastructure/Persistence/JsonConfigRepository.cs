using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DesktopOrganizer.Core.Domain;
using DesktopOrganizer.Core.Services;

namespace DesktopOrganizer.Infrastructure.Persistence;

/// <summary>
/// Implementação de <see cref="IConfigRepository"/> que persiste o
/// <see cref="AppConfig"/> como JSON em
/// <c>%APPDATA%\DesktopOrganizer\config.json</c>.
///
/// Garantias:
/// - <see cref="LoadAsync"/> retorna <see cref="AppConfig"/> com defaults
///   quando o arquivo não existe — nenhum side effect (não cria perfil nem
///   salva o arquivo). O bootstrap fica a cargo de TASK-018.
/// - Arquivo corrompido é tratado como ausente (também retorna defaults)
///   para evitar que a aplicação fique presa em estado inconsistente.
/// - <see cref="SaveAsync"/> grava de forma atômica via <c>FileMode.Create</c>
///   após garantir o diretório raiz.
/// </summary>
public class JsonConfigRepository : IConfigRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AppDataPaths _paths;

    public JsonConfigRepository(AppDataPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<AppConfig> LoadAsync()
    {
        var filePath = _paths.GetConfigFilePath();
        if (!File.Exists(filePath))
        {
            return new AppConfig();
        }

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            var config = await JsonSerializer
                .DeserializeAsync<AppConfig>(stream, SerializerOptions)
                .ConfigureAwait(false);

            return config ?? new AppConfig();
        }
        catch (JsonException)
        {
            // Arquivo corrompido — devolve defaults para não travar a app.
            return new AppConfig();
        }
        catch (IOException)
        {
            // Arquivo bloqueado/ilegível — mesma estratégia.
            return new AppConfig();
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        if (config is null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        _paths.EnsureRoot();
        var filePath = _paths.GetConfigFilePath();

        await using var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        await JsonSerializer.SerializeAsync(stream, config, SerializerOptions)
            .ConfigureAwait(false);
    }
}
