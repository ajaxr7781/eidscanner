using System.Runtime.InteropServices;
using System.Text;
using EidAgent.Exceptions;

namespace EidAgent.Services;

public interface IcaSdkClient
{
    void Initialize(IcaInitializationOptions options);

    IReadOnlyList<string> ListReaders();

    void SelectReader(string readerName);

    void ConnectCard();

    IcaReaderStatus GetReaderStatus();

    IcaCardData ReadCard();

    byte[]? GetPhoto();

    void DisconnectCard();

    void Cleanup();
}

public sealed class IcaInitializationOptions
{
    public bool ProcessMode { get; set; } = true;

    public string ConfigFilePath { get; set; } = "config_ap";
}

public enum IcaReaderStatus
{
    Ready,
    ToolkitNotInitialized,
    ReaderNotSelected,
    CardNotConnected,
    ReaderNotFound,
    CardNotPresent,
    Timeout,
    Busy,
    Unknown
}

public sealed class IcaCardData
{
    public string EidNumber { get; set; } = string.Empty;

    public string FullNameEn { get; set; } = string.Empty;

    public string Nationality { get; set; } = string.Empty;

    public DateOnly Dob { get; set; }

    public string Gender { get; set; } = string.Empty;

    public DateOnly Expiry { get; set; }

    public string? RawSignedResponseXml { get; set; }

    public string? RequestId { get; set; }
}

public sealed class NativeIcaSdkClient : IcaSdkClient
{
    public void Initialize(IcaInitializationOptions options)
    {
        var processMode = options.ProcessMode ? 1 : 0;
        var result = NativeIcaInterop.Initialize(processMode, options.ConfigFilePath ?? string.Empty);
        EnsureSuccess(result, "ICA toolkit initialization failed.");
    }

    public IReadOnlyList<string> ListReaders()
    {
        const int bufferLength = 8 * 1024;
        var buffer = new StringBuilder(bufferLength);
        var result = NativeIcaInterop.ListReaders(buffer, bufferLength);
        EnsureSuccess(result, "Unable to list ICA readers.");

        var readers = buffer.ToString()
            .Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return readers;
    }

    public void SelectReader(string readerName)
    {
        var result = NativeIcaInterop.SelectReader(readerName);
        EnsureSuccess(result, "Unable to select ICA reader.");
    }

    public void ConnectCard()
    {
        var result = NativeIcaInterop.ConnectCard();
        EnsureSuccess(result, "Failed to connect to Emirates ID card.");
    }

    public IcaReaderStatus GetReaderStatus()
    {
        var result = NativeIcaInterop.GetReaderStatus();
        return result switch
        {
            0 => IcaReaderStatus.Ready,
            1 => IcaReaderStatus.ToolkitNotInitialized,
            2 => IcaReaderStatus.ReaderNotSelected,
            3 => IcaReaderStatus.CardNotConnected,
            4 => IcaReaderStatus.ReaderNotFound,
            5 => IcaReaderStatus.CardNotPresent,
            6 => IcaReaderStatus.Timeout,
            7 => IcaReaderStatus.Busy,
            _ => IcaReaderStatus.Unknown
        };
    }

    public IcaCardData ReadCard()
    {
        const int bufferLength = 64 * 1024;
        var xmlBuffer = new StringBuilder(bufferLength);
        var result = NativeIcaInterop.ReadPublicData(xmlBuffer, bufferLength);
        EnsureSuccess(result, "Failed to read public card data from ICA SDK.");

        return IcaResponseParser.ParsePublicDataXml(xmlBuffer.ToString());
    }

    public byte[]? GetPhoto()
    {
        var length = 0;
        var probeResult = NativeIcaInterop.GetPhoto(IntPtr.Zero, ref length);
        if (probeResult != 0 || length <= 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            var readResult = NativeIcaInterop.GetPhoto(buffer, ref length);
            EnsureSuccess(readResult, "Failed to read card photo.");

            var output = new byte[length];
            Marshal.Copy(buffer, output, 0, length);
            return output;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void DisconnectCard()
    {
        var result = NativeIcaInterop.DisconnectCard();
        if (result != 0)
        {
            throw new EidAgentException(EidAgentErrorCode.InternalError, "Failed to disconnect card session.");
        }
    }

    public void Cleanup()
    {
        var result = NativeIcaInterop.Cleanup();
        if (result != 0)
        {
            throw new EidAgentException(EidAgentErrorCode.InternalError, "Failed to cleanup ICA toolkit session.");
        }
    }

    private static void EnsureSuccess(int result, string message)
    {
        if (result == 0)
        {
            return;
        }

        throw result switch
        {
            4 => new EidAgentException(EidAgentErrorCode.ReaderNotFound, message),
            5 => new EidAgentException(EidAgentErrorCode.CardNotPresent, message),
            6 => new EidAgentException(EidAgentErrorCode.Timeout, message),
            _ => new EidAgentException(EidAgentErrorCode.InternalError, $"{message} ICA code: {result}.")
        };
    }
}

internal static class NativeIcaInterop
{
    [DllImport("ica_sdk.dll", EntryPoint = "Initialize", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int Initialize(int processMode, string configFilePath);

    [DllImport("ica_sdk.dll", EntryPoint = "ListReaders", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int ListReaders(StringBuilder readersBuffer, int bufferLength);

    [DllImport("ica_sdk.dll", EntryPoint = "SelectReader", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int SelectReader(string readerName);

    [DllImport("ica_sdk.dll", EntryPoint = "ConnectCard", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ConnectCard();

    [DllImport("ica_sdk.dll", EntryPoint = "GetReaderStatus", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetReaderStatus();

    [DllImport("ica_sdk.dll", EntryPoint = "ReadPublicData", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    internal static extern int ReadPublicData(StringBuilder xmlBuffer, int bufferLength);

    [DllImport("ica_sdk.dll", EntryPoint = "GetPhoto", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetPhoto(IntPtr buffer, ref int length);

    [DllImport("ica_sdk.dll", EntryPoint = "DisconnectCard", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int DisconnectCard();

    [DllImport("ica_sdk.dll", EntryPoint = "Cleanup", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Cleanup();
}

internal static class IcaResponseParser
{
    public static IcaCardData ParsePublicDataXml(string xml)
    {
        var doc = System.Xml.Linq.XDocument.Parse(xml);

        static string GetValue(System.Xml.Linq.XDocument doc, params string[] names)
        {
            foreach (var name in names)
            {
                var element = doc.Descendants().FirstOrDefault(x =>
                    string.Equals(x.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
                if (element is not null && !string.IsNullOrWhiteSpace(element.Value))
                {
                    return element.Value.Trim();
                }
            }

            return string.Empty;
        }

        static DateOnly ParseDate(string value)
        {
            if (DateOnly.TryParse(value, out var d))
            {
                return d;
            }

            if (DateTime.TryParse(value, out var dt))
            {
                return DateOnly.FromDateTime(dt);
            }

            return default;
        }

        return new IcaCardData
        {
            EidNumber = GetValue(doc, "IdNumber", "EmiratesId", "EidNumber"),
            FullNameEn = GetValue(doc, "FullNameEnglish", "FullNameEn", "NameEnglish"),
            Nationality = GetValue(doc, "Nationality", "NationalityEnglish"),
            Dob = ParseDate(GetValue(doc, "DateOfBirth", "Dob", "BirthDate")),
            Gender = GetValue(doc, "Gender"),
            Expiry = ParseDate(GetValue(doc, "ExpiryDate", "CardExpiryDate")),
            RawSignedResponseXml = xml,
            RequestId = GetValue(doc, "RequestID")
        };
    }
}
