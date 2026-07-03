using System.Collections.Concurrent;
using System.Text.Json;

namespace ATU.Shared;

public class OfflineQueue<T> where T : class
{
    private readonly string _filePath;
    private readonly ConcurrentQueue<T> _memoryQueue = new();
    private readonly object _lock = new();

    public OfflineQueue(string appName, string queueName)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            appName);
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, $"{queueName}.json");
        LoadFromDisk();
    }

    public void Enqueue(T item)
    {
        _memoryQueue.Enqueue(item);
        Persist();
    }

    public bool TryDequeue(out T? item)
    {
        if (_memoryQueue.TryDequeue(out item))
        {
            Persist();
            return true;
        }
        return false;
    }

    public int Count => _memoryQueue.Count;
    public IReadOnlyCollection<T> ToList() => _memoryQueue.ToArray();

    private void LoadFromDisk()
    {
        if (!File.Exists(_filePath)) return;
        try
        {
            var json = File.ReadAllText(_filePath);
            var items = JsonSerializer.Deserialize<List<T>>(json);
            if (items != null)
                foreach (var i in items) _memoryQueue.Enqueue(i);
        }
        catch { /* Ignorar corrupción */ }
    }

    private void Persist()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_memoryQueue.ToArray());
                File.WriteAllText(_filePath, json);
            }
            catch { /* Ignorar errores de IO */ }
        }
    }
}