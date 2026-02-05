using EidAgent.Models;

namespace EidAgent.Services;

public class FakeEidReader : IEidReader
{
    public Task<EidReadResponse> ReadAsync(CancellationToken ct)
    {
        // Unmasked sample; API layer will mask to last-4-digits
        var eidNumber = "784-1988-1234567-1";

        return Task.FromResult(new EidReadResponse(
            EidNumberMasked: eidNumber,
            FullNameEn: "AJAY RAMACHANDRAN",
            Nationality: "INDIA",
            Dob: "1977-01-01",
            Gender: "M",
            Expiry: "2032-12-31",
            PhotoBase64: null
        ));
    }
}
