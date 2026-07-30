using Small_square_cavity_coating_machine.Models.History;

namespace Small_square_cavity_coating_machine.Services.History;

public sealed class SessionTrendStore : ISessionTrendStore
{
    private readonly Lock _gate = new();
    private readonly List<TelemetrySample> _samples = [];

    public event EventHandler<TelemetrySample>? SampleAdded;

    public void Append(TelemetrySample sample)
    {
        lock (_gate)
        {
            _samples.Add(sample);
        }

        SampleAdded?.Invoke(this, sample);
    }

    public IReadOnlyList<TelemetrySample> Snapshot()
    {
        lock (_gate)
        {
            return _samples.ToArray();
        }
    }
}
