// Conquer.IntegrationTests — Test meter factory for testing metrics.

using System.Diagnostics.Metrics;

namespace Conquer.IntegrationTests;

internal sealed class TestMeterFactory : IMeterFactory
{
    public Meter Create(MeterOptions options) => new(options.Name, options.Version);
    public void Dispose() { }
}
