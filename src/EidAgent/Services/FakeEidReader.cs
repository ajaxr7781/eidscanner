using EidAgent.Models;

namespace EidAgent.Services;

public sealed class FakeEidReader : IEidReader
{
    public Task<EidReadResponse> ReadAsync(CancellationToken cancellationToken)
    {
        var response = new EidReadResponse
        {
            EidNumberMasked = "784-****-****-1234",
            FullNameEn = "Amal Al Noor",
            Nationality = "UAE",
            Dob = new DateOnly(1990, 6, 15),
            Gender = "F",
            Expiry = new DateOnly(2030, 12, 31),
            PhotoBase64 = null
        };

        return Task.FromResult(response);
    }
}
