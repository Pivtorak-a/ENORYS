using Enorys.Abstractions;
using Enorys.Core.Social;

namespace Enorys.Domains.Survival;

/// <summary>
/// Experimental, subjective proposal based on *private* competing claims.
/// Shares are heuristic research scores, never objective probabilities of guilt.
/// </summary>
public sealed record SocialBeliefSuggestion(
    string Incident, string Suspect, string Root, double LogLikelihood,
    double Share, bool MayAccuse, bool MayTell);

/// <summary>
/// Optional survival-domain mapping from mutually exclusive private hypotheses
/// to social action candidates. Does not mutate memory or access World Authority.
/// </summary>
public static class CompetingBeliefPolicy {
    public static SocialBeliefSuggestion? Evaluate(
        AgentView actor, long cognitiveTick) {
        ArgumentNullException.ThrowIfNull(actor);
        if(cognitiveTick<0)throw new ArgumentOutOfRangeException(nameof(cognitiveTick));

        SocialBeliefSuggestion? best=null;
        foreach(var incident in actor.Evidence.Where(e=>e.Active &&
                    e.Suspect!=actor.Id && e.LogLikelihood>0 &&
                    double.IsFinite(e.LogLikelihood))
                .Select(e=>e.Incident).Distinct(StringComparer.Ordinal)
                .OrderBy(id=>id,StringComparer.Ordinal)) {
            if(string.IsNullOrWhiteSpace(incident))continue;
            var comparison=IncidentBeliefEstimator.Compare(actor.Evidence,incident,cognitiveTick);
            if(comparison.Alternatives.Count==0)continue;
            var lead=comparison.Alternatives[0];
            if(lead.Suspect==actor.Id||lead.Share<=Math.Max(.35,comparison.UnknownShare))
                continue;
            var witness=actor.Evidence.Where(e=>e.Active &&
                    e.Incident==incident && e.Suspect==lead.Suspect &&
                    double.IsFinite(e.LogLikelihood) && e.LogLikelihood>0)
                .OrderByDescending(e=>e.LogLikelihood)
                .ThenBy(e=>e.Root,StringComparer.Ordinal)
                .FirstOrDefault();
            if(witness==null)continue;

            double alternative=Math.Max(comparison.UnknownShare,
                comparison.Alternatives.Count>1?comparison.Alternatives[1].Share:0);
            double margin=lead.Share-alternative;
            bool accuse=lead.IndependentRoots>=2 && lead.Share>=.75 && margin>=.35;
            bool tell=lead.Share>=.65 && margin>=.25;
            var proposal=new SocialBeliefSuggestion(incident,lead.Suspect,
                witness.Root,witness.LogLikelihood,lead.Share,accuse,tell);
            if(best==null||proposal.Share>best.Share||
               (proposal.Share==best.Share &&
                StringComparer.Ordinal.Compare(proposal.Incident,best.Incident)<0))
                best=proposal;
        }
        return best;
    }
}
