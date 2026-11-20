using System.Runtime.InteropServices;

namespace LibMpv.Managed.Native;

internal readonly struct Utf8String : IDisposable
{
    internal Utf8String(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains('\0'))
            throw new ArgumentException("libmpv strings cannot contain embedded NUL characters.", nameof(value));

        Pointer = Marshal.StringToCoTaskMemUTF8(value);
    }

    internal nint Pointer { get; }
    public void Dispose() => Marshal.FreeCoTaskMem(Pointer);
}
