// Conquer.NpcServer — Background worker hosting NpcWorld.
// Original: my_server/NpcServer/KernelThread.cpp.

using Conquer.Runtime;
using Conquer.Shared;
using Conquer.Shared.Transport;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Conquer.NpcServer;

/// <summary>
/// Hosted background service running the 500ms NPC server loop.
/// Original: my_server/NpcServer/KernelThread.cpp KernelThread::OnProcess.
/// </summary>
public sealed class NpcServerWorker : BackgroundService
{
    private readonly NpcWorld _world;
    private readonly INpcTransport _transport;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<NpcServerWorker> _logger;

    public NpcServerWorker(
        NpcWorld world,
        INpcTransport transport,
        TimeProvider timeProvider,
        ILogger<NpcServerWorker> logger)
    {
        _world = world;
        _transport = transport;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Start background receiver loop
        var receiveTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var packet in _transport.ReceiveAsync(stoppingToken).ConfigureAwait(false))
                {
                    long nowMs = _timeProvider.GetTimestamp();
                    _world.ProcessGameServerPacket(packet, nowMs);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in NPC server receive loop");
            }
        }, stoppingToken);

        // 2. 500ms Periodic timer tick loop
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TimingConstants.NpcSystemOnTimerMs), _timeProvider);
        try
        {
            while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                long nowMs = Environment.TickCount64;
                _world.OnTimer(nowMs);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await _transport.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}
