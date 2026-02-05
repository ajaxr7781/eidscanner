namespace EidAgent.Exceptions;

public enum EidAgentErrorCode
{
    CardNotPresent,
    ReaderNotFound,
    Timeout,
    InternalError
}

public sealed class EidAgentException : Exception
{
    public EidAgentException(EidAgentErrorCode errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public EidAgentException(EidAgentErrorCode errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    public EidAgentErrorCode ErrorCode { get; }

    public string ErrorCodeValue => ErrorCode switch
    {
        EidAgentErrorCode.CardNotPresent => "card_not_present",
        EidAgentErrorCode.ReaderNotFound => "reader_not_found",
        EidAgentErrorCode.Timeout => "timeout",
        _ => "internal_error"
    };
}
