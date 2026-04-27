using System;
using System.IO;

namespace DesktopOrganizer.Infrastructure.Persistence;

/// <summary>
/// Resolve os caminhos de persistência da aplicação dentro de
/// <c>%APPDATA%\DesktopOrganizer\</c>. Cria os diretórios sob demanda quando
/// solicitados, garantindo que os repositórios não precisem replicar essa
/// lógica.
/// </summary>
public class AppDataPaths
{
    private const string AppFolderName = "DesktopOrganizer";
    private const string ProfilesFolderName = "profiles";
    private const string SnapshotsFolderName = "snapshots";
    private const string ProfileFileName = "profile.json";

    private readonly string _rootPath;

    /// <summary>
    /// Constrói usando o diretório padrão do usuário (<c>%APPDATA%</c>).
    /// </summary>
    public AppDataPaths()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))
    {
    }

    /// <summary>
    /// Constrói com um diretório base customizado. Útil para testes que devem
    /// operar em diretório temporário e nunca tocar em <c>%APPDATA%</c> real.
    /// </summary>
    /// <param name="appDataBasePath">
    /// Diretório base equivalente a <c>%APPDATA%</c>; o diretório raiz da
    /// aplicação será criado dentro dele como subpasta <c>DesktopOrganizer</c>.
    /// </param>
    public AppDataPaths(string appDataBasePath)
    {
        if (string.IsNullOrWhiteSpace(appDataBasePath))
        {
            throw new ArgumentException(
                "Diretório base não pode ser vazio.",
                nameof(appDataBasePath));
        }

        _rootPath = Path.Combine(appDataBasePath, AppFolderName);
    }

    /// <summary>
    /// Diretório raiz da aplicação (<c>%APPDATA%\DesktopOrganizer\</c>).
    /// </summary>
    public string RootPath => _rootPath;

    /// <summary>
    /// Diretório que agrupa todos os perfis
    /// (<c>%APPDATA%\DesktopOrganizer\profiles\</c>).
    /// </summary>
    public string ProfilesRoot => Path.Combine(_rootPath, ProfilesFolderName);

    /// <summary>
    /// Garante a existência do diretório raiz e o retorna.
    /// </summary>
    public string EnsureRoot()
    {
        Directory.CreateDirectory(_rootPath);
        return _rootPath;
    }

    /// <summary>
    /// Garante a existência do diretório de perfis e o retorna.
    /// </summary>
    public string EnsureProfilesRoot()
    {
        Directory.CreateDirectory(ProfilesRoot);
        return ProfilesRoot;
    }

    /// <summary>
    /// Caminho do diretório do perfil
    /// (<c>%APPDATA%\DesktopOrganizer\profiles\&lt;profileId&gt;\</c>).
    /// Não cria o diretório.
    /// </summary>
    public string GetProfileDirectory(Guid profileId)
    {
        return Path.Combine(ProfilesRoot, profileId.ToString());
    }

    /// <summary>
    /// Garante a existência do diretório do perfil e o retorna.
    /// </summary>
    public string EnsureProfileDirectory(Guid profileId)
    {
        var path = GetProfileDirectory(profileId);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Caminho do arquivo <c>profile.json</c> dentro do diretório do perfil.
    /// </summary>
    public string GetProfileFilePath(Guid profileId)
    {
        return Path.Combine(GetProfileDirectory(profileId), ProfileFileName);
    }

    /// <summary>
    /// Caminho do diretório de snapshots do perfil
    /// (<c>...\&lt;profileId&gt;\snapshots\</c>). Não cria o diretório.
    /// </summary>
    public string GetSnapshotsDirectory(Guid profileId)
    {
        return Path.Combine(GetProfileDirectory(profileId), SnapshotsFolderName);
    }

    /// <summary>
    /// Garante a existência do diretório de snapshots do perfil e o retorna.
    /// </summary>
    public string EnsureSnapshotsDirectory(Guid profileId)
    {
        var path = GetSnapshotsDirectory(profileId);
        Directory.CreateDirectory(path);
        return path;
    }
}
