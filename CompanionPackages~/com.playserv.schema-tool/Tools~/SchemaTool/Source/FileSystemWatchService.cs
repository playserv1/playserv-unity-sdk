using System.Text.Json;

namespace PlayServ.Schema.Tool;

internal sealed class FileSystemWatchService : IDisposable
{
    private readonly string _projectRoot;
    private readonly SchemaToolConfiguration _configuration;
    private readonly Action _generate;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly ManualResetEventSlim _stopped = new(false);
    private FileStream? _projectLock;
    private string _sourceState = string.Empty;
    private long _lastPollTicks;
    private long _lastChangeTicks;
    private int _pending;

    public FileSystemWatchService(
        string projectRoot,
        SchemaToolConfiguration configuration,
        Action generate)
    {
        _projectRoot = projectRoot;
        _configuration = configuration;
        _generate = generate;
        AcquireProjectLock();
    }

    public void Run()
    {
        WriteState();
        Console.CancelKeyPress += OnCancel;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        try
        {
            _sourceState = CaptureSourceState();
            _lastPollTicks = DateTime.UtcNow.Ticks;
            foreach (var root in ProjectConfiguration.ResolveWatchRoots(
                         _projectRoot,
                         _configuration))
            {
                var watcher = new FileSystemWatcher(root, "*.cs")
                {
                    IncludeSubdirectories = true,
                    NotifyFilter =
                        NotifyFilters.FileName |
                        NotifyFilters.LastWrite |
                        NotifyFilters.CreationTime |
                        NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += OnRenamed;
                _watchers.Add(watcher);
            }

            while (!_stopped.Wait(100))
            {
                PollSourceState();
                if (Volatile.Read(ref _pending) == 0)
                    continue;

                var elapsed = DateTime.UtcNow -
                              new DateTime(
                                  Interlocked.Read(ref _lastChangeTicks),
                                  DateTimeKind.Utc);
                if (elapsed < TimeSpan.FromMilliseconds(350))
                    continue;

                Interlocked.Exchange(ref _pending, 0);
                _generate();
            }
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            DeleteState();
        }
    }

    public void Dispose()
    {
        _stopped.Set();
        foreach (var watcher in _watchers)
            watcher.Dispose();
        _watchers.Clear();
        _projectLock?.Dispose();
        _projectLock = null;
        _stopped.Dispose();
    }

    private void AcquireProjectLock()
    {
        var stateDirectory = Path.Combine(_projectRoot, ".playserv");
        Directory.CreateDirectory(stateDirectory);
        var lockPath = Path.Combine(stateDirectory, "schema-tool.watch.lock");
        try
        {
            _projectLock = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "A PlayServ schema watcher is already running for this project.",
                exception);
        }
    }

    private void WriteState()
    {
        var path = GetStatePath();
        var content = JsonSerializer.Serialize(
            new
            {
                pid = Environment.ProcessId,
                toolVersion = ToolConstants.Version,
                protocolVersion = ToolConstants.ProtocolVersion,
                projectRoot = _projectRoot,
                startedAtUtc = DateTime.UtcNow.ToString("O")
            },
            new JsonSerializerOptions { WriteIndented = true });
        ProjectConfiguration.WriteAtomic(path, content + Environment.NewLine);
    }

    private void DeleteState()
    {
        var path = GetStatePath();
        if (File.Exists(path))
            File.Delete(path);
    }

    private string GetStatePath()
    {
        return Path.Combine(_projectRoot, ".playserv", "watch-state.json");
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        MarkPending();
    }

    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        MarkPending();
    }

    private void MarkPending()
    {
        Interlocked.Exchange(ref _lastChangeTicks, DateTime.UtcNow.Ticks);
        Interlocked.Exchange(ref _pending, 1);
    }

    private void PollSourceState()
    {
        var now = DateTime.UtcNow;
        var lastPoll = new DateTime(
            Interlocked.Read(ref _lastPollTicks),
            DateTimeKind.Utc);
        if (now - lastPoll < TimeSpan.FromMilliseconds(500))
            return;

        Interlocked.Exchange(ref _lastPollTicks, now.Ticks);
        var current = CaptureSourceState();
        if (string.Equals(current, _sourceState, StringComparison.Ordinal))
            return;

        _sourceState = current;
        MarkPending();
    }

    private string CaptureSourceState()
    {
        var builder = new StringBuilder();
        foreach (var path in ProjectConfiguration.EnumerateSourceFiles(
                     _projectRoot,
                     _configuration))
        {
            var info = new FileInfo(path);
            builder.Append(path);
            builder.Append(':');
            builder.Append(info.Exists ? info.Length : 0L);
            builder.Append(':');
            builder.Append(info.Exists ? info.LastWriteTimeUtc.Ticks : 0L);
            builder.Append('\n');
        }

        return builder.ToString();
    }

    private void OnCancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        _stopped.Set();
    }

    private void OnProcessExit(object? sender, EventArgs args)
    {
        _stopped.Set();
    }
}
