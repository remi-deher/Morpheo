using MessagePack;

namespace Morpheo.Core.Sync;

[MessagePackObject]
public class LogSegment
{
    [Key(0)]
    public string FileName { get; set; } = string.Empty;
    [Key(1)]
    public long StartTick { get; set; }
    [Key(2)]
    public long EndTick { get; set; }
    [Key(3)]
    public int Count { get; set; }

    public LogSegment() { }

    public LogSegment(string fileName, long startTick, long endTick, int count)
    {
        FileName = fileName;
        StartTick = startTick;
        EndTick = endTick;
        Count = count;
    }
}

[MessagePackObject]
public class LogManifest
{
    [Key(0)]
    public List<LogSegment> Segments { get; set; } = new();
}