using EidAgent.Exceptions;
using EidAgent.Models;

namespace EidAgent.Services;

public sealed class IcaEidReader : IEidReader
{
    public Task<EidReadResponse> ReadAsync(CancellationToken cancellationToken)
    {
        throw new EidAgentException(
            EidAgentErrorCode.InternalError,
            "ICA SDK integration is not implemented. Replace this stub with SDK calls.");
    }
}
