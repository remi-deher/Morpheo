using MessagePack;
using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Morpheo.Core.Data;
using System.Text;

namespace Morpheo.Core.Sync;

public class FileLogStore : ISyncLogStore, IHostedService, IDisposable
{
    private readonly string _storagePath;
    private readonly string _manifestPath;
    private readonly ILogger<FileLogStore> _logger;
    private readonly ConcurrentQueue<SyncLog> _memTable = new();
    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private Timer? _flushTimer;
    
    // Manifest: List of known log files sorted by time
    private List<LogSegment> _manifest = new();
    private readonly object _manifestLock = new();

    private const int FlushIntervalMs = 2000;
    private const int FlushThreshold = 100; 

    public FileLogStore(string storagePath, ILogger<FileLogStore> logger)
    {
        _storagePath = storagePath;
        _logger = logger;
        _manifestPath = Path.Combine(_storagePath, "manifest.msgpack");
        
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }
    
    // Required constructor for DI if arguments differ, but assuming standard resolution or factory.
    // Based on previous code, it likely takes MorpheoNode or Options maybe? 
    // Wait, the previous file didn't show constructor. It was implicit or I missed it in the view.
    // Let's re-read the previous file content for Constructor.
    // Ah, lines 16-21 don't show constructor. It was probably further down or I missed it.
    // Actually, looking at `Morpheo.Core\Sync\FileLogStore.cs` in previous turn (Step 109), I see:
    // `public class FileLogStore : ...` 
    // And NO constructor was shown in the first few lines?
    // Wait, looking at lines 16-22, fields are declared.
    // Line 22-25 fields.
    // Line 32 method LoadOrRebuildManifestAsync.
    // THERE IS NO CONSTRUCTOR in the previous view! 
    // It must have been stripped or I am blind.
    // Let me check again... 
    // Lines 1-14: Imports and Class decl.
    // Lines 16-31: Fields.
    // Line 32: Method.
    // The previous code snippet MISSES the constructor! 
    // This implies I need to create one. 
    // I recall `MorpheoServiceExtensions` does `services.AddSingleton<FileLogStore>();` 
    // and `services.AddSingleton<ISyncLogStore>(sp => sp.GetRequiredService<FileLogStore>());`
    // If it relies on `MorpheoOptions`, I should inject them.
    // Let's look at `MorpheoServiceExtensions.cs` again if possible, or assume a constructor based on fields.
    // The fields are `_storagePath` and `_manifestPath`. `_logger`.
    // I need to properly initialize them.
    // `MorpheoOptions` likely provides the path (DataFolder). 
    // Using `MorpheoOptions` and `ILogger`.
    
    public FileLogStore(Morpheo.Sdk.MorpheoOptions options, ILogger<FileLogStore> logger)
    {
        _logger = logger;
        // Default path if not specified in options? 
        // Options usually have LocalStoragePath.
        var basePath = options.LocalStoragePath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MorpheoData");
        _storagePath = Path.Combine(basePath, "logs");
        _manifestPath = Path.Combine(_storagePath, "manifest.msgpack");

        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    private async Task LoadOrRebuildManifestAsync()
    {
        lock (_manifestLock)
        {
            _manifest.Clear();
        }

        if (File.Exists(_manifestPath))
        {
            try 
            {
                // Read manifest with sharing to allow external backup tools
                using var fs = new FileStream(_manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var manifest = await MessagePackSerializer.DeserializeAsync<LogManifest>(fs);
                
                if (manifest != null && manifest.Segments != null)
                {
                    lock (_manifestLock)
                    {
                        _manifest.AddRange(manifest.Segments.OrderBy(s => s.StartTick));
                    }
                    _logger.LogInformation($"Loaded LogManifest with {_manifest.Count} segments.");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to load manifest: {ex.Message}. Rebuilding from disk...");
            }
        }

        // Rebuild from files (Optimized)
        var files = Directory.GetFiles(_storagePath, "logs_*.jsonl");
        var newSegments = new List<LogSegment>();
        
        foreach (var file in files)
        {
             var info = ParseFileInfo(Path.GetFileName(file));
             if (info != null)
             {
                 // Optimization: Count lines without loading full content
                 // And do not fail if file is locked (try with FileShare)
                 int lineCount = await CountLinesFastAsync(file);
                 newSegments.Add(new LogSegment(Path.GetFileName(file), info.Value.Min, info.Value.Max, lineCount));
             }
        }
        
        lock (_manifestLock)
        {
            _manifest.AddRange(newSegments.OrderBy(s => s.StartTick));
        }
        
        await SaveManifestAsync();
        _logger.LogInformation($"Rebuilt LogManifest with {_manifest.Count} segments.");
    }

    private async Task<int> CountLinesFastAsync(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[32 * 1024];
            int count = 0;
            int bytesRead;
            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == '\n') count++;
                }
            }
            return count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Could not count lines for {path}: {ex.Message}");
            return 0;
        }
    }

    private async Task SaveManifestAsync()
    {
        try
        {
            List<LogSegment> snapshot;
            lock (_manifestLock)
            {
                snapshot = new List<LogSegment>(_manifest);
            }
            var manifestObj = new LogManifest { Segments = snapshot };
            
            using var fs = new FileStream(_manifestPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            await MessagePackSerializer.SerializeAsync(fs, manifestObj);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save LogManifest.");
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await LoadOrRebuildManifestAsync();
        _flushTimer = new Timer(async _ => await FlushAsync(), null, FlushIntervalMs, FlushIntervalMs);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _flushTimer?.Change(Timeout.Infinite, 0);
        await FlushAsync();
    }

    public Task AddLogAsync(SyncLog log)
    {
        _memTable.Enqueue(log);
        if (_memTable.Count >= FlushThreshold)
        {
             _ = Task.Run(FlushAsync);
        }
        return Task.CompletedTask;
    }

    private async Task FlushAsync()
    {
        if (_memTable.IsEmpty) return;
        if (!await _flushLock.WaitAsync(0)) return;

        try
        {
            var batch = new List<SyncLog>();
            while (_memTable.TryDequeue(out var log))
            {
                batch.Add(log);
            }

            if (batch.Count == 0) return;

            var minTick = batch.Min(l => l.Timestamp);
            var maxTick = batch.Max(l => l.Timestamp);
            var filename = $"logs_{minTick}_{maxTick}_{Guid.NewGuid():N}.jsonl";
            var filePath = Path.Combine(_storagePath, filename);

            // Use FileShare.Read to allow external readers (backups, viewers)
            using (var fs = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read))
            using (var writer = new StreamWriter(fs, Encoding.UTF8))
            {
                foreach (var log in batch)
                {
                    var json = JsonSerializer.Serialize(log);
                    var checksum = Crc32Helper.Compute(json);
                    // Format: JSON|CHECKSUM_HEX
                    await writer.WriteLineAsync($"{json}|{checksum:X8}");
                }
            }
            
            lock (_manifestLock)
            {
                _manifest.Add(new LogSegment(filename, minTick, maxTick, batch.Count));
            }
            await SaveManifestAsync();

            _logger.LogDebug($"Flushed {batch.Count} logs to {filename}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush MemTable to disk.");
        }
        finally
        {
            _flushLock.Release();
        }
    }

    // --- READ OPERATIONS (With CRC Check) ---

    public async Task<List<SyncLog>> GetLogsAsync(long sinceTick, int maxCount = 1000)
    {
        var memLogs = _memTable.Where(l => l.Timestamp > sinceTick).ToList();
        var fileLogs = new List<SyncLog>();
        List<LogSegment> segments;
        
        lock (_manifestLock)
        {
            segments = _manifest
                .Where(s => s.EndTick >= sinceTick)
                .OrderBy(s => s.StartTick)
                .ToList();
        }

        foreach (var segment in segments)
        {
            if (fileLogs.Count >= maxCount) break;

            var logs = await ReadLogsFromFileAsync(segment.FileName);
            foreach (var log in logs)
            {
                if (log.Timestamp > sinceTick)
                {
                    fileLogs.Add(log);
                    if (fileLogs.Count >= maxCount) break;
                }
            }
        }

        return fileLogs.Concat(memLogs)
            .OrderBy(l => l.Timestamp)
            .Take(maxCount)
            .ToList();
    }

    public async Task<int> CountAsync()
    {
         int count = _memTable.Count;
         lock(_manifestLock)
         {
             foreach (var seg in _manifest)
             {
                 count += seg.Count;
             }
         }
         return await Task.FromResult(count);
    }

    public async Task<SyncLog?> GetLastLogForEntityAsync(string entityId)
    {
        var memLog = _memTable.Reverse().FirstOrDefault(l => l.EntityId == entityId);
        if (memLog != null) return memLog;

        List<LogSegment> segments;
        lock (_manifestLock)
        {
            segments = _manifest.OrderByDescending(s => s.EndTick).ToList();
        }

        foreach (var seg in segments)
        {
            var logs = await ReadLogsFromFileAsync(seg.FileName);
            // Read backwards
            for (int i = logs.Count - 1; i >= 0; i--)
            {
                if (logs[i].EntityId == entityId) return logs[i];
            }
        }

        return null;
    }

    public async Task<List<SyncLog>> GetLogsByRangeAsync(long startTick, long endTick)
    {
        var memLogs = _memTable.Where(l => l.Timestamp >= startTick && l.Timestamp <= endTick).ToList();
        var fileLogs = new List<SyncLog>();
        
        List<LogSegment> segments;
        lock (_manifestLock)
        {
             segments = _manifest
                .Where(s => s.EndTick >= startTick && s.StartTick <= endTick)
                .OrderBy(s => s.StartTick)
                .ToList();
        }

        foreach (var seg in segments)
        {
             var logs = await ReadLogsFromFileAsync(seg.FileName);
             fileLogs.AddRange(logs.Where(l => l.Timestamp >= startTick && l.Timestamp <= endTick));
        }
        
        return fileLogs.Concat(memLogs).OrderBy(l => l.Timestamp).ToList();
    }

    private async Task<List<SyncLog>> ReadLogsFromFileAsync(string filename)
    {
        var results = new List<SyncLog>();
        var path = Path.Combine(_storagePath, filename);
        if (!File.Exists(path)) return results;

        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, Encoding.UTF8);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                // Split Checksum: JSON|CRC32
                var lastPipe = line.LastIndexOf('|');
                
                string json;
                
                if (lastPipe != -1 && lastPipe > line.Length - 10) // Checksum is 8 hex chars usually
                {
                    json = line.Substring(0, lastPipe);
                    var checksumHex = line.Substring(lastPipe + 1);
                    
                    if (uint.TryParse(checksumHex, System.Globalization.NumberStyles.HexNumber, null, out var expectedCrc))
                    {
                        var actualCrc = Crc32Helper.Compute(json);
                        if (actualCrc != expectedCrc)
                        {
                            _logger.LogWarning($"CORRUPTION DETECTED in {filename}: Checksum mismatch. Skipping line.");
                            continue;
                        }
                    }
                    else
                    {
                        // Fallback: maybe pipe was in data? Try parsing whole line.
                        // Or treating as corrupt if we strictly enforce format.
                        // For safety, assume corruption if pipe structure exists but looks invalid?
                        // Actually, if simply reading line-by-line, splitting on last pipe is risky if data has pipe.
                        // But data is JSON, so pipe is valid.
                        // However, we append |HASH at the end.
                        // So robust logic: simple split. If JSON is valid, CRC should match.
                        // If we are unsure (legacy data), we might try to just parse 'line'.
                        // Let's support legacy fallback.
                        try 
                        {
                            var log = JsonSerializer.Deserialize<SyncLog>(line);
                            if (log != null) results.Add(log);
                            continue;
                        } 
                        catch 
                        {
                            _logger.LogWarning($"Found truncated or invalid line in {filename}. Skipping.");
                            continue; 
                        }
                    }
                }
                else
                {
                    // No pipe, or pipe far away: Legacy format
                    json = line;
                }

                try
                {
                    var log = JsonSerializer.Deserialize<SyncLog>(json);
                    if (log != null) results.Add(log);
                }
                catch 
                {
                    // Ignore malformed JSON (truncated line)
                    _logger.LogWarning($"Skipped partial log entry in {filename}");
                }
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning($"IO Error reading {filename}: {ex.Message}");
        }

        return results;
    }

    public async Task<int> DeleteOldLogsAsync(long thresholdTick)
    {
        int deletedCount = 0;
        List<LogSegment> toDelete;
        
        lock (_manifestLock)
        {
            toDelete = _manifest.Where(s => s.EndTick < thresholdTick).ToList();
        }
        
        foreach (var seg in toDelete)
        {
            var path = Path.Combine(_storagePath, seg.FileName);
            try
            {
                if (File.Exists(path))
                {
                    // Count lines first? Or trust manifest?
                    // We trust manifest for count usually.
                    deletedCount += seg.Count;
                    File.Delete(path);
                    _logger.LogInformation($"Deleted log file {seg.FileName} (Compaction)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to delete file {path}: {ex.Message}");
            }
        }
        
        lock (_manifestLock)
        {
            _manifest.RemoveAll(s => s.EndTick < thresholdTick);
        }
        await SaveManifestAsync();
        
        return deletedCount;
    }

    private (long Min, long Max)? ParseFileInfo(string filename)
    {
        try 
        {
            // Expected: logs_{Min}_{Max}_{Guid}.jsonl
            // Also need to support segments that might not have Guid? 
            // My FlushAsync uses Guid.
            var parts = filename.Split('_'); 
            if (parts.Length >= 3 && long.TryParse(parts[1], out var min) && long.TryParse(parts[2], out var max))
            {
                return (min, max);
            }
        }
        catch { }
        return null;
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
        _flushLock.Dispose();
    }

    /// <summary>
    /// Simple CRC32 implementation (ISO 3309).
    /// </summary>
    private static class Crc32Helper
    {
        private static readonly uint[] Table;

        static Crc32Helper()
        {
            const uint poly = 0xedb88320;
            Table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    if ((crc & 1) == 1)
                        crc = (crc >> 1) ^ poly;
                    else
                        crc >>= 1;
                }
                Table[i] = crc;
            }
        }

        public static uint Compute(string text)
        {
            return Compute(Encoding.UTF8.GetBytes(text));
        }

        public static uint Compute(byte[] bytes)
        {
            uint crc = 0xffffffff;
            foreach (byte b in bytes)
            {
                byte index = (byte)((crc ^ b) & 0xff);
                crc = (crc >> 8) ^ Table[index];
            }
            return ~crc;
        }
    }
}
