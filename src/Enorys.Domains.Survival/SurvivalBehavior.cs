using Enorys.Abstractions;
namespace Enorys.Domains.Survival;

/// <summary>Example domain-specific decision plugin: none of these item names are in ENORYS Core.</summary>
public sealed partial class SurvivalBehavior : IEnorysModule, IDecisionModule, IFastDecisionModule, IWakePolicy, ICertifiedDecisionModule {
    private readonly IChoiceKernel? choiceKernel;
    public SurvivalBehavior(IChoiceKernel? choiceKernel=null){this.choiceKernel=choiceKernel;}

    const string S="survival:";
    public string Id=>"survival:behavior-module";
    public string ModuleId=>Id;
    public void Register(IModuleRegistry registry){registry.AddDecision(this);registry.AddWakePolicy(this);}
    public int SleepTurns(AgentView a,ObservationPacket p,Decision d,ActionResult result){
        if(!result.Accepted||p.Messages.Count>0||a.Evidence.Any(x=>x.Active))return 0;
        if(d.Proposal.Kind=="survival:wait" && a.Health>65)return 1;
        if(d.Proposal.Kind=="survival:sleep" && a.Health>55 && a.Needs.GetValueOrDefault("survival:thirst")<60)return 1;
        return 0;
    }
    static int I(AgentView a,string item)=>a.Inventory.GetValueOrDefault(S+item);
    static double N(AgentView a,string need)=>a.Needs.GetValueOrDefault(S+need)/100;
    static bool Source(ObservationPacket p,string item)=>p.SourcesKnown.GetValueOrDefault(S+item);
    static int Depot(ObservationPacket p,string item)=>p.Depot.GetValueOrDefault(S+item);
    static Candidate C(AgentView a,string kind,double score,string reason,string? item=null,string? target=null,
        string? incident=null,string? root=null,string? suspect=null,double lr=0)=>
        new(new ActionProposal(a.Id,S+kind,item,target,incident,root,suspect,lr),score,reason);
    public Decision Choose(AgentView a,ObservationPacket p,IDecisionContext ctx){
        double h=N(a,"hunger"),w=N(a,"thirst"),f=N(a,"fatigue"),cold=N(a,"cold");
        var t=a.Personality;
        var candidates=new List<Candidate>(25){
            C(a,"wait",.01,"baseline"),
            C(a,"sleep",.55+2.8*f,"rest"),
            C(a,"relieve",.2+3.4*N(a,"bladder"),"sanitation"),
            C(a,"socialize",.3+1.3*N(a,"belonging")+.7*N(a,"morale"),"affiliation")
        };
        if(I(a,"water")>0)candidates.Add(C(a,"drink",.35+4*w,"hydration"));
        if(I(a,"food")>0)candidates.Add(C(a,"eat",.35+3.75*h,"nutrition"));
        if(I(a,"wood")>0)candidates.Add(C(a,"warm",.4+2.6*cold,"temperature"));
        if(I(a,"soap")>0&&I(a,"water")>0)candidates.Add(C(a,"wash",.1+2.5*N(a,"hygiene"),"wash"));
        if(I(a,"medicine")>0&&a.Infection>0)candidates.Add(C(a,"treat",2+a.Infection/25,"medical"));
        if(I(a,"bandage")>0&&a.Injury>0)candidates.Add(C(a,"bandage",2+a.Injury/25,"wound"));
        if(I(a,"dirty_water")>0&&I(a,"wood")>0)candidates.Add(C(a,"boil",1.4+1.6*w,"water prep"));
        if(I(a,"raw_food")>0)candidates.Add(C(a,"cook",1.2+1.4*h,"food prep"));
        if(Source(p,"dirty_water")&&I(a,"water")+I(a,"dirty_water")==0)
            candidates.Add(C(a,"collect_water",1+2*w,"collect"));
        if(Source(p,"raw_food")&&I(a,"food")+I(a,"raw_food")==0)
            candidates.Add(C(a,"gather_food",1+1.8*h,"collect"));
        if(Source(p,"wood")&&I(a,"wood")==0)
            candidates.Add(C(a,"gather_wood",.55+1.1*cold+.4*Convert.ToInt32(I(a,"dirty_water")>0),"fuel"));
        if(I(a,"wood")>=3&&I(a,"shelter")==0)
            candidates.Add(C(a,"build_shelter",.3+1.6*cold+1.2*N(a,"safety"),"build"));
        if(I(a,"wood")>=2&&Depot(p,"latrine")==0)
            candidates.Add(C(a,"build_latrine",.1+1.3*N(a,"hygiene")+.8*N(a,"bladder"),"build"));
        if(Depot(p,"water")>0&&w>.48)
            candidates.Add(C(a,"take",1.8+2.3*w+.45*t.Greed-.8*t.Norm,"common supply",S+"water"));
        if(Depot(p,"food")>0&&h>.53)
            candidates.Add(C(a,"take",1.6+2.2*h+.45*t.Greed-.75*t.Norm,"common supply",S+"food"));

        var planStep=ctx.PlannedStep(a,p);
        if(planStep!=null){
            double urge=a.PlanGoal==S+"feed"?h:w;
            // On the first plan turn AgentView.PlanGoal may still be null, determine from next action's effect.
            if(a.PlanGoal==null && planStep.Kind is "survival:gather_food" or "survival:cook" or "survival:eat")urge=h;
            candidates.Add(new Candidate(planStep,1.55+1.85*urge,"planned prerequisite"));
        }
        if(ctx.SocialEnabled){
            var help=a.Requests.FirstOrDefault(m=>p.Nearby.Contains(m.From)&&
                a.Inventory.GetValueOrDefault(m.Item)>0&&ctx.CanSpare(a,m.Item)&&
                ((m.Item==S+"water"?w:m.Item==S+"food"?h:0)<.55||
                 a.Inventory.GetValueOrDefault(m.Item)>1));
            if(help!=null)candidates.Add(C(a,"give",2.25+1.8*t.Generosity,"respond to request",help.Item,help.From));
            foreach(var debt in a.Debts){
                if(debt.Value<=0)continue;
                var parts=debt.Key.Split(':',2);
                // Debt key combines an agent ID and a namespaced item ID; split after the actor ID.
                if(parts.Length==2&&p.Nearby.Contains(parts[0])&&a.Inventory.GetValueOrDefault(parts[1])>0){
                    candidates.Add(C(a,"repay",2.1+1.4*t.Norm,"repay debt",parts[1],parts[0]));break;
                }
            }
            var requested=I(a,"water")==0&&w>.56?"water":I(a,"food")==0&&h>.6?"food":null;
            if(requested!=null&&p.Nearby.Count>0&&!a.Acted.Contains("ask:"+S+requested+":"+p.Day))
                candidates.Add(C(a,"ask",1.8+1.6*(requested=="water"?w:h)+.5*t.Sociability,
                    "request assistance",S+requested,p.Nearby[0]));
            var correction=a.PendingCorrections.FirstOrDefault(x=>p.Nearby.Contains(x.Recipient)&&
                !a.Reported.Contains("retract:"+x.Root+":"+x.Recipient));
            if(correction!=null)candidates.Add(C(a,"retract",3.2,"correct false report",
                target:correction.Recipient,root:correction.Root));
            var suspicion=FindSuspicion(a,p.Tick,ctx.CognitiveTickStride);
            if(suspicion!=null&&suspicion.Value.Probability>.4){
                var s=suspicion.Value;
                if(!a.Acted.Contains("investigate:"+s.Incident))
                    candidates.Add(C(a,"investigate",1.15+1.2*s.Probability+.5*t.Curiosity,
                        "uncertain claim",incident:s.Incident));
                if(s.Probability>.56&&!a.Acted.Contains("accuse:"+s.Incident+":"+s.Suspect))
                    candidates.Add(C(a,"accuse",.85+1.6*s.Probability+.35*t.Norm,
                        "social norm",target:s.Suspect,incident:s.Incident));
                string? recipient=p.Nearby.FirstOrDefault(x=>x!=s.Suspect&&!a.Reported.Contains(s.Root+":"+x));
                if(recipient!=null&&p.Tick>1)
                    candidates.Add(C(a,"tell",.8+.9*t.Sociability+.5*s.Probability,
                        "tell witness",target:recipient,incident:s.Incident,root:s.Root,suspect:s.Suspect,lr:s.LogLikelihood));
            }
        }
        if(choiceKernel!=null){
            if(candidates.Count>64)throw new InvalidOperationException("Candidate buffer exceeded.");
            Span<double> values=stackalloc double[64];
            for(int i=0;i<candidates.Count;i++){
                var c=candidates[i];
                values[i]=c.Utility+ctx.EmotionBias(a,c.Proposal.Kind,c.Proposal.Target,p.Tick);
            }
            int winner=choiceKernel.ArgMax(values[..candidates.Count]);
            if(winner<0||winner>=candidates.Count)
                throw new InvalidOperationException("Invalid backend choice.");
            var result=candidates[winner];
            return new Decision(result.Proposal,"deliberate",result.Reason,candidates.Count,values[winner]);
        }
        var selected=candidates[0];
        double best=selected.Utility+ctx.EmotionBias(a,selected.Proposal.Kind,selected.Proposal.Target,p.Tick);
        for(int i=1;i<candidates.Count;i++){
            var x=candidates[i];
            double score=x.Utility+ctx.EmotionBias(a,x.Proposal.Kind,x.Proposal.Target,p.Tick);
            if(score>best){selected=x;best=score;}
        }
        return new Decision(selected.Proposal,"deliberate",selected.Reason,candidates.Count,best);
    }
    private static (string Incident,string Suspect,double Probability,string Root,double LogLikelihood)? FindSuspicion(
        AgentView a,long tick,int stride){
        (string Incident,string Suspect,double Probability,string Root,double LogLikelihood)? result=null;
        void Inspect(IReadOnlyList<PrivateEvidence> list){
            if(list.Count==0)return;
            double logit=Math.Log(.12/.88);
            for(int j=0;j<list.Count;j++){
                var evidence=list[j];
                logit+=evidence.LogLikelihood*
                    Math.Exp(-Math.Max(0,tick-evidence.Tick)/(800.0*stride))*
                    (j==0?1:.25);
            }
            double posterior=1/(1+Math.Exp(-logit));
            if(result==null||posterior>result.Value.Probability)
                result=(list[0].Incident,list[0].Suspect,posterior,
                    list[0].Root,list[0].LogLikelihood);
        }
        if(a.HasEvidenceIndex){
            // Indexed private evidence is sorted and invalidated by AgentMind
            // only when a received message changes that actor's cognition.
            foreach(var group in a.IndexedEvidenceGroups){
                if(group.Suspect!=a.Id)Inspect(group.Records);
            }
        }else{
            // Compatibility for external plugins constructing raw AgentViews.
            foreach(var group in a.Evidence.Where(e=>e.Active&&e.Suspect!=a.Id)
                .GroupBy(e=>e.Incident+":"+e.Suspect)){
                Inspect(group.OrderByDescending(x=>x.LogLikelihood)
                    .ThenBy(x=>x.Root,StringComparer.Ordinal).ToArray());
            }
        }
        return result;
    }

    public Decision? TryFastDecision(AgentView a,ObservationPacket p,IDecisionContext ctx){
        if(ctx.HasAffect || a.Health<45||a.Infection>45||a.Injury>50||a.PlanGoal==null)return null;
        if(ctx.SocialEnabled && (a.Evidence.Any(x=>x.Active)||a.Requests.Count>0||
             a.Debts.Any(x=>x.Value>0)||a.PendingCorrections.Count>0||
             (p.Nearby.Count>0&&(N(a,"thirst")>.56&&I(a,"water")==0||
                                N(a,"hunger")>.6&&I(a,"food")==0))))return null;
        string goal=a.PlanGoal;
        if(goal=="survival:hydrate"&&N(a,"hunger")>.8||goal=="survival:feed"&&N(a,"thirst")>.8)return null;
        var plan=ctx.PlannedStep(a,p);if(plan==null)return null;
        double h=N(a,"hunger"),w=N(a,"thirst"),t=N(a,"cold");
        double planned=1.55+1.85*(goal=="survival:feed"?h:w);
        // Conservative bound: if an unenumerated competitor could win, deliberate instead.
        double competitor=Math.Max(.55+2.8*N(a,"fatigue"),
             Math.Max(.2+3.4*N(a,"bladder"),.3+1.3*N(a,"belonging")+.7*N(a,"morale")));
        void Bound(string kind,bool active,double score){if(active&&plan.Kind!="survival:"+kind)competitor=Math.Max(competitor,score);}
        Bound("drink",I(a,"water")>0,.35+4*w);
        Bound("eat",I(a,"food")>0,.35+3.75*h);
        Bound("warm",I(a,"wood")>0,.4+2.6*t);
        Bound("wash",I(a,"soap")>0&&I(a,"water")>0,.1+2.5*N(a,"hygiene"));
        Bound("treat",I(a,"medicine")>0&&a.Infection>0,2+a.Infection/25);
        Bound("bandage",I(a,"bandage")>0&&a.Injury>0,2+a.Injury/25);
        Bound("boil",I(a,"dirty_water")>0&&I(a,"wood")>0,1.4+1.6*w);
        Bound("cook",I(a,"raw_food")>0,1.2+1.4*h);
        Bound("collect_water",Source(p,"dirty_water")&&I(a,"water")+I(a,"dirty_water")==0,1+2*w);
        Bound("gather_food",Source(p,"raw_food")&&I(a,"food")+I(a,"raw_food")==0,1+1.8*h);
        Bound("gather_wood",Source(p,"wood")&&I(a,"wood")==0,.55+1.1*t+.4*Convert.ToInt32(I(a,"dirty_water")>0));
        Bound("build_shelter",I(a,"wood")>=3&&I(a,"shelter")==0,.3+1.6*t+1.2*N(a,"safety"));
        Bound("build_latrine",I(a,"wood")>=2&&Depot(p,"latrine")==0,.1+1.3*N(a,"hygiene")+.8*N(a,"bladder"));
        Bound("take",Depot(p,"water")>0&&w>.48,1.8+2.3*w+.45*a.Personality.Greed-.8*a.Personality.Norm);
        Bound("take",Depot(p,"food")>0&&h>.53,1.6+2.2*h+.45*a.Personality.Greed-.75*a.Personality.Norm);
        if(competitor>=planned-1e-12)return null;
        return new Decision(plan,"automatic","planned step dominates upper bounds",0,planned);
    }
}
