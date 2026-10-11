namespace Enorys.Core.Epistemics;

/// <summary>
/// Episode provenance registry: identical claims traced to one original root
/// never become independent evidence merely through repeated forwarding.
/// Different claims from one root still require explicit dependency modeling.
/// </summary>
public readonly record struct EvidenceClaim(
    ulong ObservationId, ulong RootId, ulong ClaimHash, double LogLikelihoodRatio);

public sealed class EvidenceLedger
{
    private readonly Dictionary<(ulong Root, ulong Claim), EvidenceClaim> _byRoot = new();
    public int IndependentClaimCount => _byRoot.Count;

    public bool TryAdd(EvidenceClaim claim)
    {
        if (claim.ObservationId == 0 || claim.RootId == 0 || claim.ClaimHash == 0 ||
            !double.IsFinite(claim.LogLikelihoodRatio))
            throw new ArgumentException("Invalid observation provenance.", nameof(claim));
        return _byRoot.TryAdd((claim.RootId, claim.ClaimHash), claim);
    }

    public IReadOnlyCollection<EvidenceClaim> IndependentClaims => _byRoot.Values;
}
