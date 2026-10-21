using Enorys.Abstractions;

namespace Enorys.Core.Social;

/// <summary>
/// One alternative in an actor-private, mutually exclusive incident hypothesis comparison.
/// Share is a normalized heuristic weight, NOT a calibrated probability of guilt.
/// </summary>
public sealed record IncidentHypothesis(string Suspect, double Share, int IndependentRoots);

/// <summary>
/// Subjective comparison of *observed* suspects plus an explicit "unknown/other" option.
/// It must not be interpreted as objective world truth or a validated Bayesian posterior.
/// </summary>
public sealed record IncidentBeliefComparison(string Incident, double UnknownShare,
    IReadOnlyList<IncidentHypothesis> Alternatives);

/// <summary>
/// Experimental stage-12 query over private evidence. Existing NPC decision policy is unchanged.
/// Assumes candidates are mutually exclusive for this particular incident; callers must not
/// apply it to incidents with multiple possible simultaneous perpetrators.
/// </summary>
public static class IncidentBeliefEstimator {
    private const double EvidenceHalfLifeScale = 800.0;
    private const double PerRootCap = 32.0;
    private const double TotalScoreCap = 80.0;
    private static readonly double CandidatePriorLogWeight = Math.Log(.12 / .88);

    public static IncidentBeliefComparison Compare(
        IReadOnlyList<PrivateEvidence> evidence, string incident, long tick) {
        ArgumentNullException.ThrowIfNull(evidence);
        if(string.IsNullOrWhiteSpace(incident))
            throw new ArgumentException("Incident ID is required", nameof(incident));
        if(tick<0)throw new ArgumentOutOfRangeException(nameof(tick));

        // One provenance root is never more than one independent source of support.
        // If the same root makes incompatible claims about the suspect, ignore it
        // rather than arbitrarily preferring either version.
        var unique=evidence.Where(e=>e.Active &&
                e.Incident==incident &&
                !string.IsNullOrWhiteSpace(e.Root) &&
                !string.IsNullOrWhiteSpace(e.Suspect) &&
                double.IsFinite(e.LogLikelihood))
            .GroupBy(e=>e.Root,StringComparer.Ordinal)
            .OrderBy(g=>g.Key,StringComparer.Ordinal)
            .Where(g=>g.Select(e=>e.Suspect).Distinct(StringComparer.Ordinal).Skip(1).Any()==false)
            .Select(g=>g.OrderByDescending(e=>Math.Abs(e.LogLikelihood))
                .ThenBy(e=>e.Tick).ThenBy(e=>e.Sender,StringComparer.Ordinal).First())
            .ToArray();

        if(unique.Length==0)
            return new IncidentBeliefComparison(incident,1,Array.Empty<IncidentHypothesis>());

        var scored=unique.GroupBy(e=>e.Suspect,StringComparer.Ordinal)
            .OrderBy(g=>g.Key,StringComparer.Ordinal)
            .Select(g=>{
                double logScore=CandidatePriorLogWeight;
                foreach(var e in g) {
                    double age=Math.Max(0.0,(double)tick-e.Tick);
                    logScore+=Math.Clamp(e.LogLikelihood,-PerRootCap,PerRootCap)
                        *Math.Exp(-age/EvidenceHalfLifeScale);
                }
                return (Suspect:g.Key, LogScore:Math.Clamp(logScore,-TotalScoreCap,TotalScoreCap),
                    RootCount:g.Count());
            }).ToArray();

        // Stable log-sum-exp normalization includes an explicit unknown hypothesis.
        // It avoids exploding weights and does not require looking at world truth.
        double maxScore=Math.Max(0,scored.Max(s=>s.LogScore));
        double unknownWeight=Math.Exp(-maxScore);
        double total=unknownWeight;
        var weights=new double[scored.Length];
        for(int i=0;i<scored.Length;i++){
            weights[i]=Math.Exp(scored[i].LogScore-maxScore);
            total+=weights[i];
        }
        var hypotheses=scored.Select((s,i)=>
                new IncidentHypothesis(s.Suspect,weights[i]/total,s.RootCount))
            .OrderByDescending(h=>h.Share)
            .ThenBy(h=>h.Suspect,StringComparer.Ordinal).ToArray();

        return new IncidentBeliefComparison(incident,unknownWeight/total,
            Array.AsReadOnly(hypotheses));
    }
}
