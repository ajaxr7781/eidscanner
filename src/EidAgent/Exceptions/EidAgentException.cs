namespace EidAgent.Exceptions;

public class EidAgentException : Exception
{
    public string Code { get; }

    public EidAgentException(string code, string message) : base(message)
    {
        Code = code;
    }

    public static EidAgentException CardNotPresent() =>
        new("card_not_present", "No Emirates ID detected on reader.");

    public static EidAgentException ReaderNotFound() =>
        new("reader_not_found", "HID reader not detected.");

    public static EidAgentException Timeout() =>
        new("timeout", "Read operation timed out.");

    public static EidAgentException InternalError(string message = "Internal error") =>
        new("internal_error", message);
}
