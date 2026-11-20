namespace LibMpv.Managed;

public sealed class MpvException : Exception
{
    internal MpvException(string operation, int errorCode, string detail)
        : base($"libmpv {operation} failed ({errorCode}): {detail}")
    {
        ErrorCode = errorCode;
        Operation = operation;
    }

    public int ErrorCode { get; }
    public string Operation { get; }
}
