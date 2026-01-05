using BenchmarkDotNet.Attributes;
using Morpheo.Sdk;
using System.Text.Json;

namespace Morpheo.Benchmarks;

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class SerializationBenchmarks
{
    private SyncLogDto _singleLog = null!;
    private List<SyncLogDto> _batchLogs = null!;
    private string _serializedSingle = null!;
    
    private readonly JsonSerializerOptions _options = new() 
    { 
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
    };

    [Params(100, 1000)]
    public int N;

    [GlobalSetup]
    public void Setup()
    {
        var complexPayload = new 
        { 
            Operation = "UpdateInventory",
            User = "Operator-42",
            Details = new 
            { 
                Items = Enumerable.Range(0, 50).Select(i => new { Sku = $"SKU-{i}", Qty = i * 2 }).ToList(),
                Location = "Warehouse-A",
                Verified = true
            },
            Meta = new Dictionary<string, string> { { "TraceId", Guid.NewGuid().ToString() } }
        };
        var jsonPayload = JsonSerializer.Serialize(complexPayload);

        _singleLog = new SyncLogDto(
            Id: Guid.NewGuid().ToString(),
            EntityId: "inv_item_999",
            EntityName: "InventoryItem",
            JsonData: jsonPayload,
            Action: "UPDATE",
            Timestamp: DateTime.UtcNow.Ticks,
            VectorClock: new Dictionary<string, long> { { "NodeA", 120 }, { "NodeB", 45 }, { "NodeC", 12 } },
            OriginNodeId: "NodeA"
        );

        _serializedSingle = JsonSerializer.Serialize(_singleLog, _options);

        _batchLogs = Enumerable.Range(0, N)
            .Select(i => _singleLog with { Id = Guid.NewGuid().ToString() })
            .ToList();
    }

    [Benchmark]
    public string Serialize_Single() => JsonSerializer.Serialize(_singleLog, _options);

    [Benchmark]
    public SyncLogDto? Deserialize_Single() => JsonSerializer.Deserialize<SyncLogDto>(_serializedSingle, _options);

    [Benchmark]
    public string Serialize_Batch() => JsonSerializer.Serialize(_batchLogs, _options);
}
