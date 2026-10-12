// Conquer.Runtime — Base class for all server loops (MapGroup, World, Socket).
// Replaces the original CThread/CKernelThread model with a Channel<InternalMessage>-based loop.
// Original pattern: thread drains its MessagePort queue → OnTimer if due → wait for next message.
// OPUS §4.1, §4.2 rules: single owner, no locks in game logic, try/catch every handler.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Conquer.Shared;
using Conquer.Shared.Messages;
using Microsoft.Extensions.Logging;

namespace Conquer.Runtime;

/// <summary>
/// Metrics tracked per loop — backlog, busy%, max handle time, slow messages.
/// OPUS §9: "Backlog per loop is the primary health metric."
/// </summary>
public sealed class LoopMetrics
{
    private readonly Histogram<double> _handleDuration;
    private readonly ObservableGauge<int> _backlog;
    private readonly Counter<long> _slowMessages;
    private readonly Counter<long> _errors;
    private int _currentBacklog;
    private double _maxHandleMs;
    private long _totalHandled;

    public LoopMetrics(IMeterFactory meterFactory, string loopName)
    {
        var meter = meterFactory.Create($"Conquer.Loop.{loopName}");
        _handleDuration = meter.CreateHistogram<double>("handle_duration_ms", "ms", "Time to handle one message");
        _backlog = meter.CreateObservableGauge("backlog", () => _currentBacklog, description: "Messages waiting in the channel");
        _slowMessages = meter.CreateCounter<long>("slow_messages", description: "Messages exceeding the slow threshold");
        _errors = meter.CreateCounter<long>("errors", description: "Exceptions caught in message handlers");
    }

    public void RecordHandle(double ms)
    {
        _handleDuration.Record(ms);
        _totalHandled++;

        // Thread-safe "max" via compare-exchange loop
        double current = Volatile.Read(ref _maxHandleMs);
        while (ms > current)
        {
            double prev = Interlocked.CompareExchange(ref _maxHandleMs, ms, current);
            if (prev == current) break;
            current = prev;
        }
    }

    public void RecordSlow() => _slowMessages.Add(1);
    public void RecordError() => _errors.Add(1);
    public void SetBacklog(int value) => Volatile.Write(ref _currentBacklog, value);
    public double MaxHandleMs => Volatile.Read(ref _maxHandleMs);
    public long TotalHandled => Volatile.Read(ref _totalHandled);
}

/// <summary>
/// Base class for all server loops. Each loop owns a <see cref="Channel{T}"/> and runs a single reader.
/// <para>
/// <b>Thread model:</b> The loop body runs on ONE thread at a time.
/// Handlers MUST NOT <c>await</c>. All I/O results are posted back as messages.
/// </para>
/// <para>
/// <b>Timer model:</b> After draining available messages, if the timer is due, <see cref="OnTimer"/> is called.
/// This mirrors the original: drain queue → OnTimer if due → wait up to THREADDELAY_MS.
/// </para>
/// </summary>
public abstract partial class GameLoop : IDisposable
{
    private readonly Channel<InternalMessage> _channel;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    protected ILogger Logger { get; }
    public LoopMetrics Metrics { get; }
    protected TimeProvider Clock { get; }

    /// <summary>Name of this loop for logging and metrics.</summary>
    public string Name { get; }

    /// <summary>The port id of this loop in the MessagePort scheme. Original: MSGPORT_*.</summary>
    public int PortId { get; }

    /// <summary>Interval between OnTimer calls in milliseconds.</summary>
    protected abstract int TimerIntervalMs { get; }

    /// <summary>Writer side of the channel — other loops use this to send messages here.</summary>
    public ChannelWriter<InternalMessage> Writer => _channel.Writer;

    protected GameLoop(string name, int portId, ILogger logger, IMeterFactory meterFactory, TimeProvider clock)
    {
        Name = name;
        PortId = portId;
        Logger = logger;
        Metrics = new LoopMetrics(meterFactory, name);
        Clock = clock;

        // OPUS §4.1: SingleReader=true, AllowSynchronousContinuations=false
        _channel = Channel.CreateUnbounded<InternalMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });
    }

    /// <summary>Start the loop on a dedicated thread.</summary>
    public void Start()
    {
        if (_loopTask is not null) throw new InvalidOperationException($"Loop {Name} already started.");
        _loopTask = Task.Factory.StartNew(
            () => RunLoop(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <summary>Stop the loop and wait for it to finish (up to CLOSETHREAD_MILLISECS).</summary>
    public async Task StopAsync()
    {
        _channel.Writer.TryComplete();
        await _cts.CancelAsync();
        if (_loopTask is not null)
        {
            await _loopTask.WaitAsync(TimeSpan.FromMilliseconds(TimingConstants.CloseThreadMs));
        }
    }

    /// <summary>Send a message to this loop. Non-blocking, thread-safe.</summary>
    public bool Post(InternalMessage message) => _channel.Writer.TryWrite(message);

    /// <summary>
    /// Handle a single internal message. Subclasses implement per-message-type dispatch.
    /// MUST NOT await. MUST NOT throw (base class wraps in try/catch).
    /// </summary>
    protected abstract void ProcessMessage(in InternalMessage message);

    /// <summary>
    /// Periodic timer callback. Called at the configured interval (e.g., 500ms for MapGroup).
    /// </summary>
    protected abstract void OnTimer();

    /// <summary>Testing hook to invoke OnTimer synchronously.</summary>
    internal void TriggerTimerForTesting() => OnTimer();

    /// <summary>Called once when the loop starts, before processing messages.</summary>
    protected virtual void OnStart() { }

    /// <summary>Called once when the loop is shutting down, after the channel is drained.</summary>
    protected virtual void OnStop() { }

    private async Task RunLoop(CancellationToken ct)
    {
        Thread.CurrentThread.Name = $"Loop-{Name}";
        OnStart();

        var reader = _channel.Reader;
        long nextTimerTick = Clock.GetTimestamp();
        var sw = new Stopwatch();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Drain all available messages
                while (reader.TryRead(out var message))
                {
                    Metrics.SetBacklog(reader.CanCount ? reader.Count : 0);

                    sw.Restart();
                    try
                    {
                        ProcessMessage(in message);
                    }
                    catch (Exception ex)
                    {
                        Metrics.RecordError();
                        LogHandlerError(ex, message.Id);
                    }
                    finally
                    {
                        message.ReturnBuffer();
                    }
                    sw.Stop();

                    double ms = sw.Elapsed.TotalMilliseconds;
                    Metrics.RecordHandle(ms);

                    if (ms > TimingConstants.SlowMessageThresholdMs)
                    {
                        Metrics.RecordSlow();
                        LogSlowMessage(message.Id, ms);
                    }
                }

                Metrics.SetBacklog(0);

                // Check timer — original pattern: if now >= nextTimer → OnTimer, auto-correct drift
                long now = Clock.GetTimestamp();
                double elapsedSecs = Clock.GetElapsedTime(nextTimerTick, now).TotalSeconds;

                if (elapsedSecs >= 0)
                {
                    // Timer is due
                    sw.Restart();
                    try
                    {
                        OnTimer();
                    }
                    catch (Exception ex)
                    {
                        Metrics.RecordError();
                        LogTimerError(ex);
                    }
                    sw.Stop();

                    double timerMs = sw.Elapsed.TotalMilliseconds;
                    Metrics.RecordHandle(timerMs);

                    if (timerMs > TimingConstants.SlowMessageThresholdMs)
                    {
                        LogSlowTimer(timerMs);
                    }

                    // Original: auto-correct if drift > TIMER_OFFSET_LIMIT
                    long intervalTicks = (long)(TimerIntervalMs * (double)TimeProvider.System.TimestampFrequency / 1000);
                    nextTimerTick += intervalTicks;

                    long newNow = Clock.GetTimestamp();
                    double driftSecs = Clock.GetElapsedTime(nextTimerTick, newNow).TotalSeconds;
                    if (driftSecs > TimingConstants.TimerOffsetLimitSecs)
                    {
                        // Original: "auto-correct" — reset next tick to now
                        nextTimerTick = newNow + intervalTicks;
                        LogTimerDriftReset(driftSecs);
                    }
                }

                // Wait for the next message or timeout (original: THREADDELAY_MS = 400 ms)
                int waitMs = Math.Min(TimingConstants.ThreadDelayMs, TimerIntervalMs);
                try
                {
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    waitCts.CancelAfter(waitMs);
                    await reader.WaitToReadAsync(waitCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // Timeout — loop back to check timer
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
        finally
        {
            // Drain remaining messages and return buffers
            while (reader.TryRead(out var remaining))
            {
                remaining.ReturnBuffer();
            }
            OnStop();
        }
    }

    // Manual logging since partial methods with custom parameter names need specific patterns
    private void LogHandlerError(Exception ex, InternalMessageId messageId)
    {
        Logger.LogError(ex, "[{LoopName}] Exception in handler for {MessageId}", Name, messageId);
    }

    private void LogTimerError(Exception ex)
    {
        Logger.LogError(ex, "[{LoopName}] Exception in OnTimer", Name);
    }

    private void LogSlowMessage(InternalMessageId messageId, double ms)
    {
        Logger.LogWarning("[{LoopName}] SLOW {MessageId} {ElapsedMs:F1}ms", Name, messageId, ms);
    }

    private void LogSlowTimer(double ms)
    {
        Logger.LogWarning("[{LoopName}] SLOW OnTimer {ElapsedMs:F1}ms", Name, ms);
    }

    private void LogTimerDriftReset(double driftSecs)
    {
        Logger.LogWarning("[{LoopName}] Timer drift {DriftSecs:F1}s > limit, resetting", Name, driftSecs);
    }

    public void Dispose()
    {
        _cts.Dispose();
    }
}
