namespace EidAgent.Models;

public record EidReadResponse(
    string EidNumberMasked,
    string FullNameEn,
    string Nationality,
    string Dob,
    string Gender,
    string Expiry,
    string? PhotoBase64
);