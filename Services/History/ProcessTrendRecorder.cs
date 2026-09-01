using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Recipes;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;

namespace Small_square_cavity_coating_machine.Services.History;

/// <summary>Serial disk work, independent of the PLC write path. Memory survives storage failures.</summary>
public sealed class ProcessTrendRecorder : IProcessTrendRecorder
{
    private sealed class BufferedRun(ProcessTrendRun run)
    {
        public ProcessTrendRun Run = run;
        public readonly List<TelemetrySample> Samples = [];
        public int Persisted;
        public bool FlushQueued;
        public string StorageError = "", ExportError = "";
    }

    private readonly Lock _gate = new();
    private readonly IProcessTrendRepository _repository;
    private readonly ITrendFileService _files;
    private readonly string _directory, _definitions;
    private readonly bool _simulated;
    private readonly TimeProvider _clock;
    private readonly Dictionary<Guid, BufferedRun> _runs = [];
    private readonly Channel<Func<Task>> _work = Channel.CreateUnbounded<Func<Task>>(new() { SingleReader = true });
    private readonly Task _worker;
    private BufferedRun? _active;
    private string _queryError = "";
    private bool _disposed;

    public ProcessTrendRecorder(IProcessTrendRepository repository, ITrendFileService files, string directory,
        IReadOnlyList<ProcessDefinition> definitions, bool simulated = false, TimeProvider? clock = null)
    {
        _repository = repository; _files = files; _directory = directory;
        _definitions = JsonSerializer.Serialize(definitions); _simulated = simulated;
        _clock = clock ?? TimeProvider.System;
        _worker = Task.Run(async () =>
        {
            await foreach (var action in _work.Reader.ReadAllAsync().ConfigureAwait(false))
                await action().ConfigureAwait(false);
        });
    }

    public event EventHandler? Changed;
    public string RecordingError
    {
        get { lock (_gate) return string.Join("；", _runs.Values.Select(r => r.Run.RecordingError)
            .Append(_queryError).Where(s => s.Length > 0).Distinct()); }
    }

    public Task BeginAsync(RecipeRunRequest request)
    {
        BufferedRun buffer;
        lock (_gate)
        {
            if (_disposed) return Task.CompletedTask;
            buffer = new(new(request.RunId, request.RecipeName, _clock.GetLocalNow(), null,
                "运行中", "", _simulated, _definitions));
            _runs.Add(request.RunId, buffer);
            _active = buffer;
        }
        Notify();
        return Enqueue(() => { Persist(buffer); return Task.CompletedTask; });
    }

    public TelemetrySample RecordSample(TelemetrySample sample)
    {
        lock (_gate)
        {
            if (_disposed || _active is not { } buffer) return sample;
            sample = sample with { RecipeRunId = buffer.Run.RunId.ToString(), RecipeName = buffer.Run.RecipeName };
            buffer.Samples.Add(sample);
            buffer.Run = buffer.Run with { SampleCount = buffer.Samples.Count };
            if (!buffer.FlushQueued)
            {
                buffer.FlushQueued = true;
                _ = Enqueue(() =>
                {
                    Persist(buffer);
                    lock (_gate) buffer.FlushQueued = false;
                    return Task.CompletedTask;
                });
            }
            return sample;
        }
    }

    public Task EndAsync(RecipeRunResult result, bool began, bool cancelled)
    {
        BufferedRun buffer;
        lock (_gate)
        {
            if (_active is null) return Task.CompletedTask;
            buffer = _active; _active = null;
            buffer.Run = buffer.Run with { EndedAt = _clock.GetLocalNow(),
                Status = !began ? "启动失败" : result.IsCompleted ? "正常完成" : cancelled ? "已取消" : "异常中止",
                FailureReason = result.FailureReason };
        }
        Notify();
        return Enqueue(async () =>
        {
            Persist(buffer);
            try
            {
                var name = SafeName(buffer.Run.RecipeName);
                var path = Path.Combine(_directory, buffer.Run.StartedAt.ToString("yyyy-MM-dd"),
                    $"{buffer.Run.StartedAt:HH-mm-ss}_{name}_{buffer.Run.RunId:N}.csv");
                await ExportBufferAsync(buffer, path).ConfigureAwait(false);
            }
            catch (Exception ex) { SetExportError(buffer, ex.Message); }
        });
    }

    private Task Enqueue(Func<Task> action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_work.Writer.TryWrite(async () =>
        {
            try { await action().ConfigureAwait(false); }
            catch (Exception ex) { lock (_gate) _queryError = "记录后台任务失败：" + ex.Message; Notify(); }
            finally { completion.TrySetResult(); }
        })) completion.TrySetResult();
        return completion.Task;
    }

    private void Persist(BufferedRun buffer)
    {
        ProcessTrendRun run;
        TelemetrySample[] pending;
        int first;
        lock (_gate)
        {
            first = buffer.Persisted;
            pending = buffer.Samples.Skip(first).ToArray();
            run = buffer.Run with { RecordingError = buffer.ExportError };
        }
        try
        {
            _repository.Save(run, pending, first);
            lock (_gate) { buffer.Persisted = first + pending.Length; buffer.StorageError = ""; UpdateError(buffer); }
        }
        catch (Exception ex)
        {
            lock (_gate) { buffer.StorageError = "数据库保存失败：" + ex.Message; UpdateError(buffer); }
        }
        Notify();
    }

    private static void UpdateError(BufferedRun buffer) => buffer.Run = buffer.Run with {
        RecordingError = string.Join("；", new[] { buffer.StorageError, buffer.ExportError }.Where(s => s.Length > 0)) };

    private void SetExportError(BufferedRun buffer, string message)
    {
        lock (_gate) { buffer.ExportError = "CSV导出失败：" + message; UpdateError(buffer); }
        Persist(buffer);
    }

    private async Task ExportBufferAsync(BufferedRun buffer, string path)
    {
        TelemetrySample[] samples;
        lock (_gate) samples = buffer.Samples.ToArray();
        await _files.SaveAsync(path, samples).ConfigureAwait(false);
        lock (_gate) { buffer.Run = buffer.Run with { ExportPath = path }; buffer.ExportError = ""; UpdateError(buffer); }
        Persist(buffer);
    }

    public async Task ExportAsync(Guid runId, string path)
    {
        BufferedRun? buffer;
        lock (_gate) buffer = _runs.GetValueOrDefault(runId);
        if (buffer is null)
        {
            var run = await Task.Run(() => _repository.Query(DateTimeOffset.MinValue, DateTimeOffset.MaxValue)
                .Single(r => r.RunId == runId)).ConfigureAwait(false);
            await _files.SaveAsync(path, await LoadSamplesAsync(runId).ConfigureAwait(false)).ConfigureAwait(false);
            try
            {
                await Task.Run(() => _repository.Save(run with { ExportPath = path, RecordingError = "" }, [], 0)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                lock (_gate) _queryError = "CSV已保存，但归档状态更新失败：" + ex.Message;
                Notify();
                throw new IOException("CSV已保存，但归档状态更新失败", ex);
            }
            Notify();
        }
        else
        {
            // Serialize exports with automatic flushes; propagate manual-export errors to the caller.
            Exception? failure = null;
            await Enqueue(async () =>
            {
                try { await ExportBufferAsync(buffer, path).ConfigureAwait(false); }
                catch (Exception ex) { failure = ex; SetExportError(buffer, ex.Message); }
            }).ConfigureAwait(false);
            if (failure is not null) throw new IOException("导出失败", failure);
        }
    }

    public async Task<IReadOnlyList<ProcessTrendRun>> QueryAsync(DateTimeOffset start, DateTimeOffset endExclusive)
    {
        IReadOnlyList<ProcessTrendRun> stored = [];
        string previousError;
        lock (_gate) previousError = _queryError;
        try
        {
            stored = await Task.Run(() => _repository.Query(start, endExclusive)).ConfigureAwait(false);
            lock (_gate) _queryError = "";
        }
        catch (Exception ex) { lock (_gate) _queryError = "归档查询失败（仅显示内存记录）：" + ex.Message; }
        bool changed;
        lock (_gate) changed = previousError != _queryError;
        if (changed) Notify();
        lock (_gate)
        {
            var merged = stored.ToDictionary(r => r.RunId);
            foreach (var buffer in _runs.Values)
                if (buffer.Run.StartedAt >= start && buffer.Run.StartedAt < endExclusive) merged[buffer.Run.RunId] = buffer.Run;
            return merged.Values.OrderByDescending(r => r.StartedAt).ToArray();
        }
    }

    public Task<IReadOnlyList<TelemetrySample>> LoadSamplesAsync(Guid runId)
    {
        lock (_gate)
            if (_runs.TryGetValue(runId, out var buffer))
                return Task.FromResult<IReadOnlyList<TelemetrySample>>(buffer.Samples.ToArray());
        return Task.Run(() => _repository.LoadSamples(runId));
    }

    private void Notify()
    {
        // Recording observers cannot interrupt sampling or PLC dispatch.
        foreach (var handler in Changed?.GetInvocationList() ?? [])
            try { ((EventHandler)handler)(this, EventArgs.Empty); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).Take(60).ToArray()).TrimEnd('.', ' ');
        return clean.Length == 0 ? "未命名配方" : clean;
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        await EndAsync(RecipeRunResult.Aborted(0, 0, "程序退出，工艺结束状态未确认"), true, true).ConfigureAwait(false);
        _work.Writer.TryComplete();
        await _worker.ConfigureAwait(false);
    }
}
