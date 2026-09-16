using System.Security.Cryptography;
using DepotManager;
using Xunit;

namespace Depot.Tests.E2E;

[Collection(PackagedE2ECollection.CollectionName)]
public sealed class PublisherContinuityAcceptanceTests
{
    [PackagedE2EFact]
    [Trait("Category", "PackagedE2E")]
    [Trait("Tier", "Smoke")]
    public void SignedManager_RejectsUnexpectedPublisherIdentity()
    {
        var signature = AuthenticodeVerifier.GetTrustedSignatureInfo(PackagedE2EEnvironment.CurrentManager);

        Assert.False(string.IsNullOrWhiteSpace(signature.Subject));
        Assert.False(string.IsNullOrWhiteSpace(signature.Thumbprint));
        Assert.Throws<CryptographicException>(() =>
            AuthenticodeVerifier.ValidateTrustedSignature(
                PackagedE2EEnvironment.CurrentManager,
                signature.Subject + ", OU=Unexpected Publisher"));
    }
}
