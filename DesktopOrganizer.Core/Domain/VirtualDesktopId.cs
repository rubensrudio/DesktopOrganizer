namespace DesktopOrganizer.Core.Domain;

/// <summary>
/// Wrapper tipado sobre <see cref="Guid"/> que identifica um virtual desktop
/// do Windows. Existe para evitar troca acidental com outros Guids no domínio.
/// </summary>
public readonly record struct VirtualDesktopId(Guid Value)
{
    public static VirtualDesktopId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
