using System.Text.Json;

namespace SidecarBridge.Core;

public sealed class LinkRecord
{
    public string Source { get; set; } = "";
    public string Destination { get; set; } = "";
    public string Video { get; set; } = "";
    public NativeFiles.Identity Identity { get; set; } = new(0, 0);
    public long Length { get; set; }
    public long ModifiedTicks { get; set; }
    public string RulesVersion { get; set; } = "";
}
public sealed class RegistryState
{
    public int SchemaVersion { get; set; } = 1;
    public List<LinkRecord> Links { get; set; } = [];
    public HashSet<string> PendingProbe { get; set; } = new(PathSafety.Comparer);
}
public sealed class Registry
{
    public RegistryState State { get; }
    private readonly string _path;
    public Registry(string directory)
    {
        _path = Path.Combine(directory, "links.json");
        State = File.Exists(_path) ? JsonSerializer.Deserialize<RegistryState>(File.ReadAllText(_path))
            ?? throw new IOException("The ownership registry is empty or invalid; refusing all changes.") : new();
        if (State.SchemaVersion != 1) throw new IOException("Unsupported ownership registry schema; refusing all changes.");
        State.PendingProbe = new(State.PendingProbe, PathSafety.Comparer);
    }
    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, State); stream.Flush(true); }
            File.Move(temporary, _path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
