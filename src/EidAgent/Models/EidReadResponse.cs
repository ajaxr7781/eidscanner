namespace EidAgent.Models;

public sealed class EidReadResponse
{
    public string EidNumberMasked { get; set; } = string.Empty;

    public string FullNameEn { get; set; } = string.Empty;

    public string Nationality { get; set; } = string.Empty;

    public DateOnly Dob { get; set; }

    public string Gender { get; set; } = string.Empty;

    public DateOnly Expiry { get; set; }

    public string? PhotoBase64 { get; set; }
}
