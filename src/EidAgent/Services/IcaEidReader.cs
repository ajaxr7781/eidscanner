using EidAgent.Exceptions;
using EidAgent.Models;
using EidAgent.Options;
using Microsoft.Extensions.Options;

namespace EidAgent.Services;

public sealed class IcaEidReader : IEidReader
{
    private readonly IcaSdkClient _sdkClient;
    private readonly ILogger<IcaEidReader> _logger;
    private readonly AgentOptions _agentOptions;

    public IcaEidReader(IcaSdkClient sdkClient, ILogger<IcaEidReader> logger, IOptions<AgentOptions> agentOptions)
    {
        _sdkClient = sdkClient;
        _logger = logger;
        _agentOptions = agentOptions.Value;
    }

    public Task<EidReadResponse> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
        var toolkitInitialized = false;
        var cardConnected = false;

=======
>>>>>>> main
        try
        {
            _sdkClient.Initialize(new IcaInitializationOptions
            {
                ProcessMode = _agentOptions.IcaProcessMode,
                ConfigFilePath = _agentOptions.IcaConfigPath
            });
<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
            toolkitInitialized = true;
=======
>>>>>>> main

            cancellationToken.ThrowIfCancellationRequested();

            var readers = _sdkClient.ListReaders();
            if (readers.Count == 0)
            {
                throw new EidAgentException(EidAgentErrorCode.ReaderNotFound, "No card readers were detected.");
            }

            var readerName = ResolveReaderName(readers, _agentOptions.IcaPreferredReaderName);
            _sdkClient.SelectReader(readerName);
            _sdkClient.ConnectCard();
<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
            cardConnected = true;
=======
>>>>>>> main

            var status = _sdkClient.GetReaderStatus();
            if (status != IcaReaderStatus.Ready)
            {
                throw MapReaderStatus(status);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var cardData = _sdkClient.ReadCard();

<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
            if (_agentOptions.ValidateSdkResponseIntegrity &&
                !string.IsNullOrWhiteSpace(cardData.RawSignedResponseXml) &&
=======
            if (!string.IsNullOrWhiteSpace(cardData.RawSignedResponseXml) &&
>>>>>>> main
                !string.IsNullOrWhiteSpace(cardData.RequestId))
            {
                var hasValidRequest = IcaSdkIntegrationHelpers.CompareRequestId(
                    cardData.RequestId,
                    cardData.RawSignedResponseXml);
                var hasValidSignature = IcaSdkIntegrationHelpers.VerifySignature(cardData.RawSignedResponseXml);

                if (!hasValidRequest || !hasValidSignature)
                {
                    throw new EidAgentException(EidAgentErrorCode.InternalError, "ICA response integrity validation failed.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var photoBytes = _sdkClient.GetPhoto();

            _logger.LogInformation("ICA read completed successfully using reader {ReaderName}.", readerName);

            var response = new EidReadResponse
            {
                EidNumberMasked = cardData.EidNumber,
                FullNameEn = cardData.FullNameEn,
                Nationality = cardData.Nationality,
                Dob = cardData.Dob,
                Gender = cardData.Gender,
                Expiry = cardData.Expiry,
                PhotoBase64 = photoBytes is null ? null : Convert.ToBase64String(photoBytes)
            };

            return Task.FromResult(response);
        }
<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
        catch (DllNotFoundException ex)
        {
            throw new EidAgentException(
                EidAgentErrorCode.ReaderNotFound,
                "ICA SDK native library 'ica_sdk.dll' was not found. Ensure vendor SDK DLLs are deployed with the service.",
                ex);
        }
=======
>>>>>>> main
        finally
        {
            try
            {
<<<<<<< codex/create-.net-8-emirates-id-agent-service-cyqy04
                if (cardConnected)
                {
                    _sdkClient.DisconnectCard();
                }

                if (toolkitInitialized)
                {
                    _sdkClient.Cleanup();
                }
=======
                _sdkClient.DisconnectCard();
                _sdkClient.Cleanup();
>>>>>>> main
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ICA cleanup encountered a non-fatal error.");
            }
        }
    }

    private static string ResolveReaderName(IReadOnlyList<string> readers, string preferredReader)
    {
        if (string.IsNullOrWhiteSpace(preferredReader))
        {
            return readers[0];
        }

        var match = readers.FirstOrDefault(r =>
            string.Equals(r, preferredReader, StringComparison.OrdinalIgnoreCase));

        return match ?? readers[0];
    }

    private static EidAgentException MapReaderStatus(IcaReaderStatus status)
    {
        return status switch
        {
            IcaReaderStatus.CardNotPresent => new EidAgentException(EidAgentErrorCode.CardNotPresent, "Card is not present on the reader."),
            IcaReaderStatus.CardNotConnected => new EidAgentException(EidAgentErrorCode.CardNotPresent, "Card is not connected."),
            IcaReaderStatus.ReaderNotFound => new EidAgentException(EidAgentErrorCode.ReaderNotFound, "Reader device not found."),
            IcaReaderStatus.ReaderNotSelected => new EidAgentException(EidAgentErrorCode.ReaderNotFound, "No reader selected."),
            IcaReaderStatus.Timeout => new EidAgentException(EidAgentErrorCode.Timeout, "Timed out waiting for card read."),
            _ => new EidAgentException(EidAgentErrorCode.InternalError, $"Unexpected reader status: {status}")
        };
    }
}
