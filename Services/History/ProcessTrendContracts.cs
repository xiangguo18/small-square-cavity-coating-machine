using Small_square_cavity_coating_machine.Models.History;
using Small_square_cavity_coating_machine.Services.Recipes;

namespace Small_square_cavity_coating_machine.Services.History;

public interface IProcessTrendRepository
{
    void Save(ProcessTrendRun run, IReadOnlyList<TelemetrySample> samples, int firstSequence);
    IReadOnlyList<ProcessTrendRun> Query(DateTimeOffset start, DateTimeOffset endExclusive);
    IReadOnlyList<TelemetrySample> LoadSamples(Guid runId);
}

public interface IProcessTrendRecorder : IAsyncDisposable
{
    event EventHandler? Changed;
    string RecordingError { get; }
    Task BeginAsync(RecipeRunRequest request);
    Task EndAsync(RecipeRunResult result, bool began, bool cancelled);
    TelemetrySample RecordSample(TelemetrySample sample);
    Task<IReadOnlyList<ProcessTrendRun>> QueryAsync(DateTimeOffset start, DateTimeOffset endExclusive);
    Task<IReadOnlyList<TelemetrySample>> LoadSamplesAsync(Guid runId);
    Task ExportAsync(Guid runId, string path);
}
