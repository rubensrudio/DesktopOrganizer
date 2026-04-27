using DesktopOrganizer.Core.Domain;

namespace DesktopOrganizer.Core.Services;

/// <summary>
/// Persistência de <see cref="AppConfig"/> em <c>config.json</c> dentro de
/// <c>%APPDATA%\DesktopOrganizer\</c>.
///
/// <see cref="LoadAsync"/> retorna um <see cref="AppConfig"/> com valores
/// padrão quando o arquivo não existir — a criação de perfil "default" e
/// demais detalhes de bootstrap NÃO são responsabilidade deste repositório
/// (ver TASK-018).
/// </summary>
public interface IConfigRepository
{
    Task<AppConfig> LoadAsync();
    Task SaveAsync(AppConfig config);
}
