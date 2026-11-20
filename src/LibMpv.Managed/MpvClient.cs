using System.Runtime.InteropServices;
using System.Threading.Channels;
using LibMpv.Managed.Native;

namespace LibMpv.Managed;

/// <summary>An independent libmpv playback session. Dispose the session after use.</summary>
public sealed class MpvClient : IAsyncDisposable
{
    private readonly NativeLibraryApi _native;
    private readonly nint _handle;
    private readonly object _gate = new();
    private readonly Dictionary<ulong, TaskCompletionSource<MpvValue>> _pending = new();
    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Channel<MpvEvent> _events = Channel.CreateBounded<MpvEvent>(
        new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true });
    private ulong _requestId;
    private bool _stopping;
    private int _reading;

    private MpvClient(NativeLibraryApi native, nint handle)
    {
        _native = native;
        _handle = handle;
    }

    public string LibraryPath => _native.Path;
    public Version ClientApiVersion => _native.ApiVersion;

    public static unsafe MpvClient Create(MpvClientOptions? options = null)
    {
        var native = NativeLibraryApi.Load(options?.LibraryPath);
        nint handle = 0;
        try
        {
            handle = native.Create();
            if (handle == 0)
                throw new InvalidOperationException("libmpv could not create a playback session.");

            SetInitialOption(native, handle, "config", "no");
            if (options?.Options is { } initialOptions)
            {
                foreach (var (name, value) in initialOptions)
                    SetInitialOption(native, handle, name, value);
            }

            native.ThrowOnError("initialize", native.Initialize(handle));
            if (options?.LogLevel is { } logLevel)
            {
                using var level = new Utf8String(logLevel);
                native.ThrowOnError("request log messages", native.RequestLogMessages(handle, level.Pointer));
            }

            var client = new MpvClient(native, handle);
            new Thread(client.RunEventLoop) { IsBackground = true, Name = "LibMpv.Managed events" }.Start();
            return client;
        }
        catch
        {
            if (handle != 0)
                native.TerminateDestroy(handle);

            native.Dispose();
            throw;
        }
    }

    private static unsafe void SetInitialOption(NativeLibraryApi native, nint handle, string name, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var nativeName = new Utf8String(name);
        using var nativeValue = new Utf8String(value);
        native.ThrowOnError($"set option '{name}'", native.SetOption(handle, nativeName.Pointer, nativeValue.Pointer));
    }

    /// <summary>Completes when libmpv replies. Cancellation ends the wait for a submitted command.</summary>
    public async Task<MpvValue> CommandAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
            throw new ArgumentException("Specify a libmpv command name.", nameof(arguments));

        var command = arguments.ToArray();
        ArgumentException.ThrowIfNullOrWhiteSpace(command[0]);
        cancellationToken.ThrowIfCancellationRequested();
        ulong id;
        TaskCompletionSource<MpvValue> completion;
        lock (_gate)
        {
            ThrowIfStopping();
            cancellationToken.ThrowIfCancellationRequested();
            id = checked(++_requestId);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending.Add(id, completion);
            try
            {
                SubmitCommand(id, command);
            }
            catch
            {
                _pending.Remove(id);
                throw;
            }
        }

        try
        {
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
                _pending.Remove(id);
        }
    }

    private unsafe void SubmitCommand(ulong id, string[] command)
    {
        var pointers = new nint[command.Length + 1];
        try
        {
            for (var i = 0; i < command.Length; i++)
                pointers[i] = new Utf8String(command[i]).Pointer;

            fixed (nint* argv = pointers)
                _native.ThrowOnError($"submit command '{command[0]}'", _native.CommandAsync(_handle, id, argv));
        }
        finally
        {
            foreach (var pointer in pointers)
                Marshal.FreeCoTaskMem(pointer);
        }
    }

    public unsafe MpvValue GetProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var nativeName = new Utf8String(name);
        lock (_gate)
        {
            ThrowIfStopping();
            MpvNode node = default;
            _native.ThrowOnError($"get property '{name}'", _native.GetProperty(_handle, nativeName.Pointer, MpvFormat.Node, &node));
            try
            {
                return NativeValues.Copy(node);
            }
            finally
            {
                _native.FreeNodeContents(&node);
            }
        }
    }

    /// <summary>Sets a scalar property using its native type.</summary>
    public unsafe void SetProperty(string name, MpvValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(value);
        using var nativeName = new Utf8String(name);
        lock (_gate)
        {
            ThrowIfStopping();
            var status = value switch
            {
                MpvValue.Text text => SetStringProperty(nativeName.Pointer, text.Value),
                MpvValue.Flag flag => SetScalarProperty(nativeName.Pointer, MpvFormat.Flag, flag.Value ? 1 : 0),
                MpvValue.Integer integer => SetScalarProperty(nativeName.Pointer, MpvFormat.Int64, integer.Value),
                MpvValue.Number number => SetScalarProperty(nativeName.Pointer, MpvFormat.Double, number.Value),
                _ => throw new ArgumentException("Expected a string, flag, integer or number.", nameof(value)),
            };
            _native.ThrowOnError($"set property '{name}'", status);
        }
    }

    private unsafe int SetStringProperty(nint name, string value)
    {
        using var nativeValue = new Utf8String(value);
        var address = nativeValue.Pointer;
        return _native.SetProperty(_handle, name, MpvFormat.String, &address);
    }

    private unsafe int SetScalarProperty<T>(nint name, MpvFormat format, T value) where T : unmanaged =>
        _native.SetProperty(_handle, name, format, &value);

    public unsafe ulong ObserveProperty(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        using var nativeName = new Utf8String(name);
        lock (_gate)
        {
            ThrowIfStopping();
            var id = checked(++_requestId);
            _native.ThrowOnError($"observe property '{name}'", _native.ObserveProperty(_handle, id, nativeName.Pointer, MpvFormat.Node));
            return id;
        }
    }

    public unsafe bool UnobserveProperty(ulong observationId)
    {
        lock (_gate)
        {
            ThrowIfStopping();
            var status = _native.UnobserveProperty(_handle, observationId);
            _native.ThrowOnError("unobserve property", status);
            return status > 0;
        }
    }

    /// <summary>Reads managed event snapshots. A session supports one active reader.</summary>
    public async IAsyncEnumerable<MpvEvent> ReadEventsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _reading, 1) != 0)
            throw new InvalidOperationException("Only one event reader may be active per session.");

        try
        {
            await foreach (var item in _events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                yield return item;
        }
        finally
        {
            Volatile.Write(ref _reading, 0);
        }
    }

    public unsafe ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (!_stopping)
            {
                _stopping = true;
                _native.Wakeup(_handle);
            }
        }

        return new ValueTask(_finished.Task);
    }

    private unsafe void RunEventLoop()
    {
        Exception? failure = null;
        try
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_stopping)
                        break;
                }

                var item = *_native.WaitEvent(_handle, -1);
                if (!ProcessEvent(item))
                    break;
            }
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            lock (_gate)
            {
                _stopping = true;
                var closing = failure ?? new ObjectDisposedException(nameof(MpvClient));
                foreach (var request in _pending.Values)
                    request.TrySetException(closing);

                _pending.Clear();
            }

            _native.TerminateDestroy(_handle);
            _native.Dispose();
            _events.Writer.TryComplete(failure);
            _finished.TrySetResult();
        }
    }

    private unsafe bool ProcessEvent(NativeEvent item)
    {
        switch (item.Id)
        {
            case MpvEventId.CommandReply:
                CompleteCommand(item);
                break;
            case MpvEventId.StartFile:
                Publish(new MpvEvent.Started(*(long*)item.Data));
                break;
            case MpvEventId.EndFile:
                var end = *(NativeEndFile*)item.Data;
                Publish(new MpvEvent.Ended(end.Reason, end.Error, end.PlaylistEntryId));
                break;
            case MpvEventId.FileLoaded:
                Publish(new MpvEvent.FileLoaded());
                break;
            case MpvEventId.PlaybackRestart:
                Publish(new MpvEvent.PlaybackRestarted());
                break;
            case MpvEventId.PropertyChange:
                var property = *(NativePropertyEvent*)item.Data;
                var value = property.Format == MpvFormat.None ? MpvValue.Unavailable : NativeValues.Copy(*(MpvNode*)property.Data);
                Publish(new MpvEvent.PropertyChanged(item.UserData, NativeValues.ReadString(property.Name), value));
                break;
            case MpvEventId.LogMessage:
                var log = *(NativeLogMessage*)item.Data;
                Publish(new MpvEvent.LogMessage(NativeValues.ReadString(log.Prefix), NativeValues.ReadString(log.Level),
                    NativeValues.ReadString(log.Text)));
                break;
            case MpvEventId.Shutdown:
                Publish(new MpvEvent.Shutdown());
                return false;
            case MpvEventId.QueueOverflow:
                throw new InvalidOperationException("The native libmpv event queue overflowed.");
        }

        return true;
    }

    private unsafe void CompleteCommand(NativeEvent item)
    {
        lock (_gate)
        {
            if (!_pending.TryGetValue(item.UserData, out var completion))
                return;

            if (item.Error < 0)
                completion.TrySetException(_native.Error("execute command", item.Error));
            else
                completion.TrySetResult(item.Data == 0 ? MpvValue.Unavailable : NativeValues.Copy(*(MpvNode*)item.Data));

            _pending.Remove(item.UserData);
        }
    }

    private void Publish(MpvEvent item)
    {
        if (!_events.Writer.TryWrite(item))
            throw new InvalidOperationException("The managed libmpv event queue overflowed. Read session events as they arrive.");
    }

    private void ThrowIfStopping() => ObjectDisposedException.ThrowIf(_stopping, this);
}
