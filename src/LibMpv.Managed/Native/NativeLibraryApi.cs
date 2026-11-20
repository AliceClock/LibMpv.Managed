using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LibMpv.Managed.Native;

internal sealed unsafe partial class NativeLibraryApi : IDisposable
{
    private nint _module;

    internal string Path { get; }
    internal Version ApiVersion { get; }

    internal readonly delegate* unmanaged[Cdecl]<nint> Create;
    internal readonly delegate* unmanaged[Cdecl]<nint, int> Initialize;
    internal readonly delegate* unmanaged[Cdecl]<nint, void> TerminateDestroy;
    internal readonly delegate* unmanaged[Cdecl]<nint, nint, nint, int> SetOption;
    internal readonly delegate* unmanaged[Cdecl]<nint, ulong, nint*, int> CommandAsync;
    internal readonly delegate* unmanaged[Cdecl]<nint, nint, MpvFormat, void*, int> GetProperty;
    internal readonly delegate* unmanaged[Cdecl]<nint, nint, MpvFormat, void*, int> SetProperty;
    internal readonly delegate* unmanaged[Cdecl]<nint, ulong, nint, MpvFormat, int> ObserveProperty;
    internal readonly delegate* unmanaged[Cdecl]<nint, ulong, int> UnobserveProperty;
    internal readonly delegate* unmanaged[Cdecl]<nint, nint, int> RequestLogMessages;
    internal readonly delegate* unmanaged[Cdecl]<nint, double, NativeEvent*> WaitEvent;
    internal readonly delegate* unmanaged[Cdecl]<nint, void> Wakeup;
    internal readonly delegate* unmanaged[Cdecl]<MpvNode*, void> FreeNodeContents;
    private readonly delegate* unmanaged[Cdecl]<int, nint> _errorString;

    private NativeLibraryApi(nint module, string path)
    {
        _module = module;
        Path = path;
        // C unsigned long is 32 bits on Windows.
        var version = ((delegate* unmanaged[Cdecl]<uint>)Export("mpv_client_api_version"))();
        ApiVersion = new Version((int)(version >> 16), (int)(version & 0xffff));
        if (ApiVersion.Major != 2 || ApiVersion.Minor < 6)
            throw new NotSupportedException($"'{path}' provides libmpv Client API {ApiVersion}. A 2.x API from 2.6 onward is required.");

        Create = (delegate* unmanaged[Cdecl]<nint>)Export("mpv_create");
        Initialize = (delegate* unmanaged[Cdecl]<nint, int>)Export("mpv_initialize");
        TerminateDestroy = (delegate* unmanaged[Cdecl]<nint, void>)Export("mpv_terminate_destroy");
        SetOption = (delegate* unmanaged[Cdecl]<nint, nint, nint, int>)Export("mpv_set_option_string");
        CommandAsync = (delegate* unmanaged[Cdecl]<nint, ulong, nint*, int>)Export("mpv_command_async");
        GetProperty = (delegate* unmanaged[Cdecl]<nint, nint, MpvFormat, void*, int>)Export("mpv_get_property");
        SetProperty = (delegate* unmanaged[Cdecl]<nint, nint, MpvFormat, void*, int>)Export("mpv_set_property");
        ObserveProperty = (delegate* unmanaged[Cdecl]<nint, ulong, nint, MpvFormat, int>)Export("mpv_observe_property");
        UnobserveProperty = (delegate* unmanaged[Cdecl]<nint, ulong, int>)Export("mpv_unobserve_property");
        RequestLogMessages = (delegate* unmanaged[Cdecl]<nint, nint, int>)Export("mpv_request_log_messages");
        WaitEvent = (delegate* unmanaged[Cdecl]<nint, double, NativeEvent*>)Export("mpv_wait_event");
        Wakeup = (delegate* unmanaged[Cdecl]<nint, void>)Export("mpv_wakeup");
        FreeNodeContents = (delegate* unmanaged[Cdecl]<MpvNode*, void>)Export("mpv_free_node_contents");
        _errorString = (delegate* unmanaged[Cdecl]<int, nint>)Export("mpv_error_string");
    }

    internal static NativeLibraryApi Load(string? explicitPath)
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("LibMpv.Managed requires a Windows x64 process.");

        var path = ResolvePath(explicitPath);
        const uint searchDllDirectory = 0x00000100;
        const uint searchSystem32 = 0x00000800;
        var module = LoadLibraryEx(path, 0, searchDllDirectory | searchSystem32);
        if (module == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            var cause = new Win32Exception(error);
            var detail = $"Could not load '{path}' (Windows error {error}: {cause.Message}).";
            if (error == 193)
                throw new BadImageFormatException(detail, cause);

            throw new DllNotFoundException(detail, cause);
        }

        try
        {
            return new NativeLibraryApi(module, path);
        }
        catch
        {
            NativeLibrary.Free(module);
            throw;
        }
    }

    private static string ResolvePath(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            if (!System.IO.Path.IsPathFullyQualified(explicitPath))
                throw new ArgumentException("Expected an absolute libmpv DLL path.", nameof(explicitPath));

            return System.IO.Path.GetFullPath(explicitPath);
        }

        var appDirectory = AppContext.BaseDirectory;
        var candidates = new[]
        {
            System.IO.Path.Combine(appDirectory, "libmpv-2.dll"),
            System.IO.Path.Combine(appDirectory, "runtimes", "win-x64", "native", "libmpv-2.dll"),
        };
        return Array.Find(candidates, File.Exists) ?? throw new DllNotFoundException(
            $"libmpv was not found. Install LibMpv.Managed.Native.WinX64 or specify MpvClientOptions.LibraryPath. Searched: {string.Join(", ", candidates)}");
    }

    internal MpvException Error(string operation, int status) =>
        new(operation, status, NativeValues.ReadString(_errorString(status)));

    internal void ThrowOnError(string operation, int status)
    {
        if (status < 0)
            throw Error(operation, status);
    }

    private nint Export(string name) =>
        NativeLibrary.TryGetExport(_module, name, out var export)
            ? export
            : throw new EntryPointNotFoundException($"'{Path}' does not export {name}.");

    public void Dispose()
    {
        var module = Interlocked.Exchange(ref _module, 0);
        if (module != 0)
            NativeLibrary.Free(module);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "LoadLibraryExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint LoadLibraryEx(string fileName, nint fileHandle, uint flags);
}
