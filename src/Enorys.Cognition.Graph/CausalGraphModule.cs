using Enorys.Abstractions;
namespace Enorys.Cognition.Graph;

/// <summary>
/// Per-agent incremental, provenance-preserving cognition graph. Contains
/// only filtered observations, private beliefs and registered affect outputs;
/// never has a World Authority reference. DAG stages are
/// observation -> belief -> affect (optional) -> goal -> decision.
/// </summary>
public sealed class CausalGraphModule : IEnorysModule,ICausalGraphModule {
    private sealed class State {
        public readonly Dictionary<string,CausalNode> Nodes=new(StringComparer.Ordinal);
        public readonly List<CausalEdge> Edges=new();
        public readonly HashSet<(string From,string To)> EdgeKeys=new();
        public readonly HashSet<string> SeenEvents=new(StringComparer.Ordinal);
        public readonly Queue<string> Roots=new();
        public readonly Dictionary<string,List<string>> RootIndex=new(StringComparer.Ordinal);
        public readonly Dictionary<string,List<string>> GoalIndex=new(StringComparer.Ordinal);
        public readonly Dictionary<string,double> GoalDecay=new(StringComparer.Ordinal);
        public readonly Queue<string> Decisions=new();
        public long Revision,Recomputed,Unchanged,Invalidated;
    }
    private readonly Dictionary<string,CausalRule[]> byEvent;
    private readonly int maxRoots,maxDecisions;
    private readonly double biasScale;
    private readonly Dictionary<string,State> agents=new(StringComparer.Ordinal);
    public string Id=>"enorys.cognition:causal-graph";
    public string ModuleId=>Id;
    public long TotalRecomputed=>agents.Values.Sum(s=>s.Recomputed);
    public long TotalUnchanged=>agents.Values.Sum(s=>s.Unchanged);
    public long TotalInvalidated=>agents.Values.Sum(s=>s.Invalidated);
    public long TotalNodes=>agents.Values.Sum(s=>s.Nodes.Count);
    public CausalGraphModule(IEnumerable<string> packs,double biasScale=1){
        if(!double.IsFinite(biasScale)||biasScale<0||biasScale>1)
            throw new ArgumentOutOfRangeException(nameof(biasScale));
        var loaded=CausalRules.Load(packs.ToArray());
        byEvent=loaded.Rules.GroupBy(r=>r.EventType,StringComparer.Ordinal)
            .ToDictionary(g=>g.Key,g=>g.ToArray(),StringComparer.Ordinal);
        maxRoots=loaded.MaxRoots;
        maxDecisions=loaded.MaxDecisions;
        this.biasScale=biasScale;
    }
    public void Register(IModuleRegistry registry)=>registry.AddCausalGraph(this);
    private State For(string id){
        if(!agents.TryGetValue(id,out var state)){state=new State();agents.Add(id,state);}
        return state;
    }
    private static string Identity(params string[] parts)=>
        string.Concat(parts.Select(p=>p.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)+":"+p));
    private static string RootFor(Message m)=>
        m.Root??m.From+"/"+m.Type+"/"+m.Tick+"/"+m.Item;
    private static string TargetFor(CausalRule rule,Message m)=>
        rule.Target=="suspect"?m.Suspect??"":m.From;
    private static double Logistic(double x)=>x>=0
        ?1/(1+Math.Exp(-x)):Math.Exp(x)/(1+Math.Exp(x));

    private static void AddEdge(State state,string from,string to,string kind){
        if(state.EdgeKeys.Add((from,to)))state.Edges.Add(new CausalEdge(from,to,kind));
    }
    private static void Upsert(State state,CausalNode node){
        if(state.Nodes.TryGetValue(node.Id,out var prior)){
            if(prior==node){state.Unchanged++;return;}
            state.Nodes[node.Id]=node;state.Recomputed++;state.Revision++;
        }else{
            state.Nodes.Add(node.Id,node);state.Recomputed++;state.Revision++;
            if(!state.RootIndex.TryGetValue(node.Root,out var ids)){
                ids=new List<string>();state.RootIndex.Add(node.Root,ids);
            }
            ids.Add(node.Id);
            if(node.Stage=="goal"&&node.ActionKind!=null){
                if(!state.GoalIndex.TryGetValue(node.ActionKind,out var goals)){
                    goals=new List<string>();state.GoalIndex.Add(node.ActionKind,goals);
                }
                goals.Add(node.Id);
            }
        }
    }
    private static void ForgetNode(State state,string id){
        if(!state.Nodes.Remove(id,out var node))return;
        state.Invalidated++;state.Revision++;
        if(node.Stage=="goal")state.GoalDecay.Remove(id);
        if(node.Stage=="goal"&&node.ActionKind!=null&&
            state.GoalIndex.TryGetValue(node.ActionKind,out var goals))
            goals.Remove(id);
        state.Edges.RemoveAll(e=>e.From==id||e.To==id);
        state.EdgeKeys.RemoveWhere(e=>e.From==id||e.To==id);
    }
    private void Prune(State state){
        while(state.Roots.Count>maxRoots){
            string root=state.Roots.Dequeue();
            state.SeenEvents.Remove(root);
            if(state.RootIndex.Remove(root,out var ids)){
                foreach(string id in ids)ForgetNode(state,id);
            }
        }
        while(state.Decisions.Count>maxDecisions){
            string old=state.Decisions.Dequeue();
            ForgetNode(state,old);
        }
    }
    public void Observe(AgentView actor,ObservationPacket observations,
        IReadOnlyList<EmotionActivation> privateAffect,long cognitiveTick){
        if(observations.Messages.Count==0)return;
        State state=For(actor.Id);
        foreach(Message message in observations.Messages){
            string root=RootFor(message);
            if(root.Length is <1 or >256)continue;
            if(message.Type=="retract"){
                // Only the private cognitive memory can authorize this correction.
                // An objective world event alone never invalidates the belief.
                bool authorized=actor.Evidence.Any(e=>e.Root==root&&
                    e.Sender==message.From&&!e.Active);
                if(!authorized||!state.RootIndex.TryGetValue(root,out var ids))continue;
                foreach(string id in ids){
                    if(state.Nodes.TryGetValue(id,out var prior)&&prior.Active&&
                       prior.Stage is "belief" or "affect" or "goal")
                        Upsert(state,prior with {Active=false,Tick=cognitiveTick});
                }
                continue;
            }
            if(!byEvent.TryGetValue(message.Type,out var matches))continue;
            if(message.Type=="evidence"&&!actor.Evidence.Any(e=>
                   e.Root==root&&e.Active&&e.Suspect==message.Suspect))continue;
            if(!state.SeenEvents.Add(root)){state.Unchanged++;continue;}
            state.Roots.Enqueue(root);
            string eventId=Identity(actor.Id,root,"observation");
            Upsert(state,new CausalNode(eventId,actor.Id,root,"observation",
                message.From,1,true,cognitiveTick));
            foreach(var rule in matches){
                string subject=TargetFor(rule,message);
                if(string.IsNullOrEmpty(subject))continue;
                string beliefId=Identity(actor.Id,root,rule.Id,"belief");
                double belief;
                if(rule.RequireEvidence){
                    var record=actor.Evidence.FirstOrDefault(e=>e.Root==root&&e.Active&&
                        e.Suspect==subject);
                    if(record==null)continue;
                    belief=Logistic(Math.Log(.12/.88)+record.LogLikelihood);
                }else{
                    // Relationship appraisal derived from the actor's private
                    // trust value; not from global truth about other actors.
                    belief=Math.Clamp((actor.Trust.GetValueOrDefault(subject)+100)/200,0,1);
                }
                Upsert(state,new CausalNode(beliefId,actor.Id,root,"belief",
                    subject,belief,true,cognitiveTick));
                AddEdge(state,eventId,beliefId,"observed-support");

                string parent=beliefId;
                double emotionStrength=0;
                if(rule.Affect!=null){
                    foreach(var e in privateAffect)
                        if(e.EvidenceRoot==root&&e.TargetId==subject&&e.EmotionId==rule.Affect)
                            emotionStrength+=e.Intensity;
                    if(emotionStrength>0){
                        string affectId=Identity(actor.Id,root,rule.Id,"affect");
                        Upsert(state,new CausalNode(affectId,actor.Id,root,"affect",
                            subject,Math.Clamp(emotionStrength,0,1),true,cognitiveTick));
                        AddEdge(state,beliefId,affectId,"private-appraisal");
                        parent=affectId;
                    }
                }
                double strength=Math.Clamp((belief+emotionStrength*.4)*rule.Gain,0,1);
                if(strength<=0)continue;
                string goalId=Identity(actor.Id,root,rule.Id,"goal");
                Upsert(state,new CausalNode(goalId,actor.Id,root,"goal",
                    subject,strength,true,cognitiveTick,rule.Action));
                state.GoalDecay[goalId]=rule.Decay;
                AddEdge(state,parent,goalId,"motivates");
            }
            Prune(state);
        }
    }
    public double ActionBias(AgentView actor,string actionKind,string? target,long tick){
        if(!agents.TryGetValue(actor.Id,out var state)||
           !state.GoalIndex.TryGetValue(actionKind,out var ids))return 0;
        double score=0;
        foreach(string id in ids){
            var goal=state.Nodes[id];
            if(!goal.Active||(target!=null&&goal.Subject!=target))continue;
            // Definition-specific lazy decay; updating the clock never
            // invalidates or rebuilds otherwise unchanged causal nodes.
            double decay=state.GoalDecay[id];
            score+=goal.Strength*Math.Exp(-decay*Math.Max(0,tick-goal.Tick));
        }
        return Math.Clamp(score*biasScale,0,.5);
    }
    public void RecordDecision(AgentView actor,Decision decision,
        ActionResult outcome,long cognitiveTick){
        if(!agents.TryGetValue(actor.Id,out var state)||
           !state.GoalIndex.TryGetValue(decision.Proposal.Kind,out var ids))return;
        CausalNode? active=null;
        foreach(string candidateId in ids){
            var g=state.Nodes[candidateId];
            if(!g.Active||(decision.Proposal.Target!=null&&
                g.Subject!=decision.Proposal.Target))continue;
            if(active==null||g.Strength>active.Strength||
               (g.Strength==active.Strength &&
                StringComparer.Ordinal.Compare(g.Id,active.Id)<0))
                active=g;
        }
        if(active==null)return;
        string idNew=Identity(actor.Id,active.Root,"decision",
            cognitiveTick.ToString(System.Globalization.CultureInfo.InvariantCulture),
            decision.Proposal.Kind);
        if(state.Nodes.ContainsKey(idNew))return;
        Upsert(state,new CausalNode(idNew,actor.Id,active.Root,"decision",
            decision.Proposal.Target??active.Subject,Math.Clamp(decision.Score/5,0,1),
            outcome.Accepted,cognitiveTick,decision.Proposal.Kind));
        AddEdge(state,active.Id,idNew,outcome.Accepted?"motivated-action":"rejected-proposal");
        state.Decisions.Enqueue(idNew);
        Prune(state);
    }
    public CausalExplanation Explain(string agentId,string decisionNodeId){
        if(!agents.TryGetValue(agentId,out var state)||
           !state.Nodes.TryGetValue(decisionNodeId,out var selected)||
           selected.AgentId!=agentId||selected.Stage!="decision")
            return new CausalExplanation(agentId,decisionNodeId,[],[],false);
        var incoming=state.Edges.ToLookup(e=>e.To,StringComparer.Ordinal);
        var frontier=new Queue<string>();
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var links=new List<CausalEdge>();
        frontier.Enqueue(decisionNodeId);
        bool reached=false;
        while(frontier.Count>0){
            var id=frontier.Dequeue();
            if(!seen.Add(id))continue;
            if(!state.Nodes.TryGetValue(id,out var node))continue;
            if(node.Stage=="observation")reached=true;
            foreach(var edge in incoming[id].OrderBy(e=>e.From,StringComparer.Ordinal)){
                links.Add(edge);
                frontier.Enqueue(edge.From);
            }
        }
        return new CausalExplanation(agentId,decisionNodeId,
            Array.AsReadOnly(seen.Select(id=>state.Nodes[id])
                .OrderBy(x=>x.Stage,StringComparer.Ordinal).ThenBy(x=>x.Id,StringComparer.Ordinal)
                .ToArray()),
            Array.AsReadOnly(links.OrderBy(e=>e.From,StringComparer.Ordinal)
                .ThenBy(e=>e.To,StringComparer.Ordinal).ToArray()),reached);
    }
    public CausalSnapshot Inspect(string agentId){
        if(!agents.TryGetValue(agentId,out var state))
            return new CausalSnapshot(agentId,0,[],[],0,0,0);
        return new CausalSnapshot(agentId,state.Revision,
            Array.AsReadOnly(state.Nodes.Values.OrderBy(n=>n.Id,StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(state.Edges.OrderBy(e=>e.From,StringComparer.Ordinal)
                .ThenBy(e=>e.To,StringComparer.Ordinal).ToArray()),
            state.Recomputed,state.Unchanged,state.Invalidated);
    }
}
