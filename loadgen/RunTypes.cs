namespace Bmt.LoadGen;

/// <summary>One unit of work: read/insert/delete keyed on a single request id.</summary>
public readonly record struct TaskDescriptor(long Id);

/// <summary>Thread-safe operation counters aggregated for the run summary.</summary>
public sealed class RunCounters
{
    private long _finds, _inserts, _deletes;
    private long _findErrors, _insertErrors, _deleteErrors;
    private long _retries, _poolExhaustion, _backpressure;
    private long _jobsDispatched, _tasksDispatched;
    private long _queueDepth;

    public void IncOp(OpKind op)
    {
        switch (op)
        {
            case OpKind.Find: Interlocked.Increment(ref _finds); break;
            case OpKind.Insert: Interlocked.Increment(ref _inserts); break;
            case OpKind.Delete: Interlocked.Increment(ref _deletes); break;
        }
    }

    public void IncError(OpKind op)
    {
        switch (op)
        {
            case OpKind.Find: Interlocked.Increment(ref _findErrors); break;
            case OpKind.Insert: Interlocked.Increment(ref _insertErrors); break;
            case OpKind.Delete: Interlocked.Increment(ref _deleteErrors); break;
        }
    }

    public void IncRetry() => Interlocked.Increment(ref _retries);
    public void IncPoolExhaustion() => Interlocked.Increment(ref _poolExhaustion);
    public void IncBackpressure() => Interlocked.Increment(ref _backpressure);
    public void IncJob() => Interlocked.Increment(ref _jobsDispatched);
    public void AddTasks(long n) => Interlocked.Add(ref _tasksDispatched, n);

    public void IncQueue() => Interlocked.Increment(ref _queueDepth);
    public void DecQueue() => Interlocked.Decrement(ref _queueDepth);
    public long ReadQueue() => Interlocked.Read(ref _queueDepth);

    public CountersSnapshot Snapshot() => new()
    {
        Finds = Interlocked.Read(ref _finds),
        Inserts = Interlocked.Read(ref _inserts),
        Deletes = Interlocked.Read(ref _deletes),
        FindErrors = Interlocked.Read(ref _findErrors),
        InsertErrors = Interlocked.Read(ref _insertErrors),
        DeleteErrors = Interlocked.Read(ref _deleteErrors),
        Retries = Interlocked.Read(ref _retries),
        PoolExhaustion = Interlocked.Read(ref _poolExhaustion),
        DispatcherBackpressure = Interlocked.Read(ref _backpressure),
        JobsDispatched = Interlocked.Read(ref _jobsDispatched),
        TasksDispatched = Interlocked.Read(ref _tasksDispatched),
    };
}

/// <summary>Immutable counter values captured at end of run.</summary>
public sealed class CountersSnapshot
{
    public long Finds { get; set; }
    public long Inserts { get; set; }
    public long Deletes { get; set; }
    public long FindErrors { get; set; }
    public long InsertErrors { get; set; }
    public long DeleteErrors { get; set; }
    public long Retries { get; set; }
    public long PoolExhaustion { get; set; }
    public long DispatcherBackpressure { get; set; }
    public long JobsDispatched { get; set; }
    public long TasksDispatched { get; set; }
}

/// <summary>Workload operation kinds.</summary>
public enum OpKind
{
    Find,
    Insert,
    Delete,
}
