namespace Enorys.Core.Epistemics;

/// <summary>
/// Independent, non-exclusive causes. LikelihoodRatio describes external evidence
/// for a cause being active vs inactive. Model assumptions must be checked by callers.
/// </summary>
public readonly record struct Cause(double Prior, double Strength, double LikelihoodRatio = 1d);
public sealed record NoisyOrPosterior(
    double[] Causes,
    double NoListedCause,
    double LeakActivated,
    double EvidenceLogNormalizer);

public static class NoisyOr
{
    /// <summary>Exact enumeration for up to eight binary causes, stabilized in log space.</summary>
    public static NoisyOrPosterior Solve(ReadOnlySpan<Cause> causes, double leak)
    {
        if (!double.IsFinite(leak) || leak < 0 || leak > 1)
            throw new ArgumentOutOfRangeException(nameof(leak));
        if (causes.Length > 8)
            throw new ArgumentOutOfRangeException(nameof(causes), "At most eight causes.");
        foreach (var c in causes)
        {
            if (!double.IsFinite(c.Prior) || c.Prior < 0 || c.Prior > 1 ||
                !double.IsFinite(c.Strength) || c.Strength < 0 || c.Strength > 1 ||
                !double.IsFinite(c.LikelihoodRatio) || c.LikelihoodRatio <= 0)
                throw new ArgumentOutOfRangeException(nameof(causes), "Invalid cause probability, strength, or LR.");
        }

        var count = 1 << causes.Length;
        var logWeights = new double[count];
        var logPriorsWithEvidence = new double[count];
        var max = double.NegativeInfinity;
        for (int mask = 0; mask < count; mask++)
        {
            double logPrior = 0;
            double fail = 1 - leak;
            bool possible = true;
            for (int i = 0; i < causes.Length; i++)
            {
                var c = causes[i];
                bool on = (mask & (1 << i)) != 0;
                double p = on ? c.Prior : 1 - c.Prior;
                if (p == 0)
                {
                    possible = false;
                    break;
                }
                logPrior += Math.Log(p);
                if (on)
                {
                    logPrior += Math.Log(c.LikelihoodRatio);
                    fail *= (1 - c.Strength);
                }
            }
            logPriorsWithEvidence[mask] = possible ? logPrior : double.NegativeInfinity;
            double effectProbability = 1 - fail;
            logWeights[mask] = possible && effectProbability > 0
                ? logPrior + Math.Log(effectProbability)
                : double.NegativeInfinity;
            max = Math.Max(max, logWeights[mask]);
        }

        if (double.IsNegativeInfinity(max))
            throw new InvalidOperationException("Conditioning on an impossible effect.");

        double denominator = 0;
        double noCause = 0;
        double leakMass = 0;
        var posterior = new double[causes.Length];
        for (int mask = 0; mask < count; mask++)
        {
            double scaled = double.IsNegativeInfinity(logWeights[mask])
                ? 0 : Math.Exp(logWeights[mask] - max);
            denominator += scaled;
            if (mask == 0) noCause += scaled;
            for (int i = 0; i < causes.Length; i++)
                if ((mask & (1 << i)) != 0) posterior[i] += scaled;
            // Leak is an independent latent mechanism. Leak success implies the effect,
            // but "no listed cause" does not imply leak was activated.
            if (leak > 0 && !double.IsNegativeInfinity(logPriorsWithEvidence[mask]))
                leakMass += Math.Exp(logPriorsWithEvidence[mask] + Math.Log(leak) - max);
        }
        for (int i = 0; i < posterior.Length; i++)
            posterior[i] /= denominator;
        return new NoisyOrPosterior(posterior, noCause / denominator,
            leakMass / denominator, max + Math.Log(denominator));
    }
}
