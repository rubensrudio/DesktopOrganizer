using System;

namespace DesktopOrganizer.Core.Domain;

/// <summary>
/// Configuração geral da aplicação persistida em config.json.
/// </summary>
public class AppConfig
{
    public Guid ActiveProfileId { get; set; }
    public Guid? DefaultBootSnapshotId { get; set; }
    public bool StartupEnabled { get; set; }
    public int RestoreTimeoutSeconds { get; set; } = 30;
}
