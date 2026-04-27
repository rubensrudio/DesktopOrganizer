using System;

namespace DesktopOrganizer.Core.Domain;

/// <summary>
/// Perfil de contexto que agrupa snapshots (ex.: "Trabalho", "Dev").
/// </summary>
public class Profile
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
