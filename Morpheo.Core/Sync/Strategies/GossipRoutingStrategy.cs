using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync.Strategies;

/// <summary>
/// Implements the Gossip (Epidemic) routing strategy with QoS support.
/// Uses Priority Queues to prioritize Critical/High messages over Normal/Low.
/// </summary>
public class GossipRoutingStrategy : ISyncRoutingStrategy, IDisposable
{
    private const int Fanout = 3;
    private readonly ILogger<GossipRoutingStrategy> _logger;
    private readonly Channel<GossipTask>[] _queues;
    private readonly CancellationTokenSource _cts;
    private readonly Task _processingTask;

    public GossipRoutingStrategy(ILogger<GossipRoutingStrategy> logger)
    {
        _logger = logger;
        _cts = new CancellationTokenSource();
        
        // Initialize 4 channels (Critical=0 to Low=3)
        _queues = new Channel<GossipTask>[4];
        for (int i = 0; i < 4; i++)
        {
            // Unbounded for now, or Bounded with DropWrite/Wait? Unbounded is safer for not blocking callers.
            _queues[i] = Channel.CreateUnbounded<GossipTask>();
        }

        _processingTask = Task.Run(ProcessQueuesAsync);
    }

    private record GossipTask(
        SyncLogDto Log, 
        List<PeerInfo> Candidates, 
        Func<PeerInfo, SyncLogDto, Task<bool>> SendFunc
    );

    /// <inheritdoc/>
    public Task PropagateAsync(
        SyncLogDto log,
        IEnumerable<PeerInfo> candidates,
        Func<PeerInfo, SyncLogDto, Task<bool>> sendFunc)
    {
        var priorityIndex = (int)log.Priority;
        if (priorityIndex < 0 || priorityIndex >= _queues.Length)
        {
            priorityIndex = (int)SyncPriority.Normal;
        }

        var task = new GossipTask(log, candidates.ToList(), sendFunc);
        
        // Fast enqueue
        _queues[priorityIndex].Writer.TryWrite(task);
        
        return Task.CompletedTask;
    }

    private async Task ProcessQueuesAsync()
    {
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                bool processedWork = false;

                // Priority Sweep: Always check Critical (0) first, then High (1)...
                for (int i = 0; i < _queues.Length; i++)
                {
                    var reader = _queues[i].Reader;
                    if (reader.TryRead(out var item))
                    {
                        await ProcessItemAsync(item);
                        processedWork = true;
                        
                        // If we processed Critical or High, we loop back to start 
                        // to ensure we don't starve high priority while processing lower ones?
                        // Simple approach: One item per loop pass, prioritizing lower index.
                        // If we found work in Queue 0, break and restart loop to check Queue 0 again.
                        if (i < 2) // For Critical/High, restart immediately
                        {
                            break; 
                        }
                    }
                }

                if (!processedWork)
                {
                    // Wait for new data on ANY channel
                    // We can wait on all readers.
                    var waitTasks = _queues.Select(q => q.Reader.WaitToReadAsync(token).AsTask()).ToArray();
                    await Task.WhenAny(waitTasks);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gossip Queue failure");
        }
    }

    private async Task ProcessItemAsync(GossipTask item)
    {
        if (item.Candidates.Count == 0) return;

        // Randomly select 'Fanout' peers
        var selectedPeers = item.Candidates
            .OrderBy(_ => Random.Shared.Next())
            .Take(Fanout)
            .ToList();

        // Process sequentially or parallel? 
        // Parallel per item to speed up throughput
        var tasks = selectedPeers.Select(async peer => 
        {
            try
            {
                await item.SendFunc(peer, item.Log);
            }
            catch
            {
                // Ignore transient
            }
        });

        await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
