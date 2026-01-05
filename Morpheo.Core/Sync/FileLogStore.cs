using System.Collections.Concurrent;
using System.Text.Json;
using Morpheo.Sdk;

namespace Morpheo.Core.Sync;

/// <summary>
/// A file-based persistence store for sync logs.
/// Mimics a Write-Ahead Log (WAL) or simple append-only file storage.
/// </summary>
public class FileLogStore : IDisposable
{
    private readonly string _basePath;
    private readonly string _logFile;
    private readonly string _manifestFile;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private FileStream? _fileStream;
    private StreamWriter? _writer;
    private bool _started;

    public FileLogStore(string basePath)
    {
        _basePath = basePath;
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
        _logFile = Path.Combine(_basePath, "sync.log");
        _manifestFile = Path.Combine(_basePath, "manifest.json");
    }

    public async Task StartAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (_started) return;

            // Open log file in Append mode
            _fileStream = new FileStream(_logFile, FileMode.Append, FileAccess.Write, FileShare.Read);
            _writer = new StreamWriter(_fileStream) { AutoFlush = true }; // AutoFlush for safety

            // Update manifest
            await UpdateManifestAsync();

            _started = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!_started) return;

            if (_writer != null)
            {
                await _writer.FlushAsync();
                await _writer.DisposeAsync();
                _writer = null;
            }

            if (_fileStream != null)
            {
                await _fileStream.DisposeAsync();
                _fileStream = null;
            }

            _started = false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task AddLogAsync(SyncLogDto log)
    {
        if (!_started) throw new InvalidOperationException("Store not started");
        
        await _lock.WaitAsync();
        try
        {
            var line = JsonSerializer.Serialize(log);
            if (_writer != null)
            {
                await _writer.WriteLineAsync(line);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<SyncLogDto>> GetLogsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_logFile)) return new List<SyncLogDto>();

            var logs = new List<SyncLogDto>();
            // Use FileShare.ReadWrite to allow reading while open (though we usually read at startup)
            using var fs = new FileStream(_logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    try
                    {
                        var log = JsonSerializer.Deserialize<SyncLogDto>(line);
                        if (log != null) logs.Add(log);
                    }
                    catch
                    {
                        // Corruption handling: ignore bad lines
                    }
                }
            }
            return logs;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task UpdateManifestAsync()
    {
        var manifest = new
        {
            LastOpen = DateTime.UtcNow,
            Version = "1.0"
        };
        var json = JsonSerializer.Serialize(manifest);
        await File.WriteAllTextAsync(_manifestFile, json);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
        _lock.Dispose();
    }
}
