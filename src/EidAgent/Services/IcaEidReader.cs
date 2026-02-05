using EidAgent.Exceptions;
using EidAgent.Models;

namespace EidAgent.Services;

public class IcaEidReader : IEidReader
{
    public Task<EidReadResponse> ReadAsync(CancellationToken ct)
    {
        // TODO: implement ICA SDK read here.
        throw EidAgentException.ReaderNotFound();
    }
}
