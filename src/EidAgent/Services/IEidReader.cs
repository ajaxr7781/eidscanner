using EidAgent.Models;

namespace EidAgent.Services;

public interface IEidReader
{
    Task<EidReadResponse> ReadAsync(CancellationToken cancellationToken);
}
