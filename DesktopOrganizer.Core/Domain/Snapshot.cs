namespace DesktopOrganizer.Core.Domain;

public class Snapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public Guid ProfileId { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<WindowEntry> Windows { get; set; } = new();
}
