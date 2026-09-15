using System.Text;

namespace LiveClaude.Core.Logging;

/// <summary>Append-only text log with a single .1 rollover file, safe for concurrent writers.</summary>
public sealed class RollingLogWriter : IDisposable
{
    private readonly string _path;
    private readonly long _maxBytes;
    private readonly object _gate = new();
    private StreamWriter? _writer;

    public RollingLogWriter(string path, int maxSizeMb)
    {
        _path = path;
        _maxBytes = Math.Max(1, maxSizeMb) * 1024L * 1024L;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
    }

    public string Path => _path;

    public void Write(string line)
    {
        lock (_gate)
        {
            try
            {
                EnsureWriter();
                _writer!.WriteLine($"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {line}");
                _writer.Flush();

                if (_writer.BaseStream.Length > _maxBytes)
                    Roll();
            }
            catch (IOException)
            {
                // Logging must never break supervision.
            }
        }
    }

    public IReadOnlyList<string> Tail(int lines)
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                    return [];

                _writer?.Flush();

                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var buffer = new Queue<string>(lines);

                while (reader.ReadLine() is { } line)
                {
                    if (buffer.Count == lines)
                        buffer.Dequeue();
                    buffer.Enqueue(line);
                }

                return buffer.ToArray();
            }
            catch (IOException)
            {
                return [];
            }
        }
    }

    private void EnsureWriter()
    {
        _writer ??= new StreamWriter(
            new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
            Encoding.UTF8);
    }

    private void Roll()
    {
        _writer?.Dispose();
        _writer = null;

        var archive = _path + ".1";
        if (File.Exists(archive))
            File.Delete(archive);
        File.Move(_path, archive);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
