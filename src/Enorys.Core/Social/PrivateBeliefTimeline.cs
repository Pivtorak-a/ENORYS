using Enorys.Abstractions;

namespace Enorys.Core.Social;

/// <summary>
/// An immutable point-in-time record of one actor's private alternative beliefs.
/// The shares are experimental heuristic weights, not calibrated probabilities.
/// </summary>
public sealed record PrivateBeliefRevision(
    string AgentId, string Incident, long Tick, double UnknownShare,
    IReadOnlyList<IncidentHypothesis> Alternatives);

/// <summary>
/// Optional, bounded research journal of evidence-driven belief revisions.
/// It is actor-private: only an AgentView and its own delivered messages are read.
/// It never changes NPC decisions, world state, evidence, or trust.
/// </summary>
public sealed class PrivateBeliefTimeline {
    private sealed class ActorHistory {
        public readonly List<PrivateBeliefRevision> Revisions=new();
        public readonly Dictionary<string,PrivateEvidence[]> Fingerprints=
            new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string,ActorHistory> actors=new(StringComparer.Ordinal);
    private readonly int maxRevisionsPerAgent;

    public PrivateBeliefTimeline(int maxRevisionsPerAgent=64) {
        if(maxRevisionsPerAgent is <1 or >2048)
            throw new ArgumentOutOfRangeException(nameof(maxRevisionsPerAgent));
        this.maxRevisionsPerAgent=maxRevisionsPerAgent;
    }

    public void Observe(AgentView actor, ObservationPacket packet) {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(packet);
        if(packet.Messages.Count==0)return;

        // Resolve retractions through the actor's OWN memory; a correction
        // message is not an instruction to read the authoritative incident.
        var affected=new SortedSet<string>(StringComparer.Ordinal);
        foreach(var message in packet.Messages) {
            if(message.Type=="evidence" && !string.IsNullOrWhiteSpace(message.Incident))
                affected.Add(message.Incident);
            else if(message.Type=="retract" && message.Root!=null) {
                foreach(var e in actor.Evidence)
                    if(e.Root==message.Root)affected.Add(e.Incident);
            }
        }
        if(affected.Count==0)return;

        if(!actors.TryGetValue(actor.Id,out var history)){
            history=new ActorHistory();
            actors.Add(actor.Id,history);
        }

        foreach(string incident in affected) {
            var fingerprint=actor.Evidence.Where(e=>e.Incident==incident)
                .OrderBy(e=>e.Root,StringComparer.Ordinal)
                .ThenBy(e=>e.Suspect,StringComparer.Ordinal)
                .ThenBy(e=>e.Sender,StringComparer.Ordinal)
                .ThenBy(e=>e.Tick).ToArray();

            // A duplicate rumor, an unauthorized retraction, or an empty
            // observation is not a new belief revision. Age-based decay can
            // be queried independently using IncidentBeliefEstimator.Compare.
            if(fingerprint.Length==0)continue;
            if(history.Fingerprints.TryGetValue(incident,out var prior) &&
                prior.SequenceEqual(fingerprint))continue;

            var result=IncidentBeliefEstimator.Compare(actor.Evidence,incident,packet.Tick);
            var entry=new PrivateBeliefRevision(actor.Id,incident,packet.Tick,
                result.UnknownShare,result.Alternatives);
            history.Revisions.Add(entry);
            history.Fingerprints[incident]=fingerprint;

            // Limit both retained transitions and their incident fingerprints.
            while(history.Revisions.Count>maxRevisionsPerAgent) {
                var evicted=history.Revisions[0];
                history.Revisions.RemoveAt(0);
                if(!history.Revisions.Any(r=>r.Incident==evicted.Incident))
                    history.Fingerprints.Remove(evicted.Incident);
            }
        }
    }

    public IReadOnlyList<PrivateBeliefRevision> Inspect(string agentId) {
        if(string.IsNullOrWhiteSpace(agentId))
            throw new ArgumentException("Agent ID required",nameof(agentId));
        return actors.TryGetValue(agentId,out var actor)
            ?Array.AsReadOnly(actor.Revisions.ToArray())
            :Array.Empty<PrivateBeliefRevision>();
    }

    /// <summary>Privileged research export; do not expose to ordinary agents.</summary>
    public IReadOnlyList<PrivateBeliefRevision> InspectAll()=>Array.AsReadOnly(
        actors.Values.SelectMany(a=>a.Revisions)
            .OrderBy(r=>r.Tick)
            .ThenBy(r=>r.AgentId,StringComparer.Ordinal)
            .ThenBy(r=>r.Incident,StringComparer.Ordinal).ToArray());

    public long RetainedRevisions=>actors.Values.Sum(a=>a.Revisions.Count);
}
