// Conquer.StressTests — Test meter factory for stress testing metrics.

using System.Diagnostics.Metrics;

namespace Conquer.StressTests;

internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options.Name, options.Version);
    public void Dispose() { }
}
