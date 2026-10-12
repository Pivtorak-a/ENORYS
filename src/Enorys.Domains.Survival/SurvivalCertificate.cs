using Enorys.Abstractions;
namespace Enorys.Domains.Survival;

/// <summary>
/// Exact lightweight winner proof for a subset of non-interfering decisions.
/// Enumerates every candidate's precise score numerically, without allocating
/// the list, but only if planner/social/affect dependencies are provably inert.
/// No action is omitted, ever; a new proof is required on the next actor turn.
/// </summary>
public sealed partial class SurvivalBehavior {
    public ActionChoiceCertificate? TryProve(
        AgentView a,ObservationPacket p,IDecisionContext ctx){
        if(ctx.HasCausalGraph||!ctx.NoPlanCanActivate(a)||!ctx.IsAffectNeutral(a,p.Tick))
            return null;
        if(ctx.SocialEnabled){
            if(a.Evidence.Any(e=>e.Active)||a.Requests.Count>0||
               a.Debts.Any(e=>e.Value>0)||a.PendingCorrections.Count>0)
                return null;
            // Any potential social request requires the full interaction logic.
            bool requestWater=I(a,"water")==0&&N(a,"thirst")>.56;
            bool requestFood=I(a,"food")==0&&N(a,"hunger")>.6;
            if(p.Nearby.Count>0&&(requestWater||requestFood))return null;
        }
        double h=N(a,"hunger"),w=N(a,"thirst"),
            f=N(a,"fatigue"),cold=N(a,"cold");
        var trait=a.Personality;
        string winner="wait",reason="baseline";
        string? item=null;
        double best=double.NegativeInfinity,second=double.NegativeInfinity;
        int count=0;
        void Score(string kind,double utility,string why,bool enabled=true,string? namedItem=null){
            if(!enabled)return;
            if(!double.IsFinite(utility))throw new InvalidOperationException("Nonfinite decision utility");
            count++;
            if(utility>best){
                second=best;best=utility;winner=kind;reason=why;item=namedItem;
            }else if(utility>second)second=utility;
        }
        // The order, exact arithmetic expressions and optional candidate
        // conditions mirror SurvivalBehavior.Choose. Ties abstain.
        Score("wait",.01,"baseline");
        Score("sleep",.55+2.8*f,"rest");
        Score("relieve",.2+3.4*N(a,"bladder"),"sanitation");
        Score("socialize",.3+1.3*N(a,"belonging")+.7*N(a,"morale"),"affiliation");
        Score("drink",.35+4*w,"hydration",I(a,"water")>0);
        Score("eat",.35+3.75*h,"nutrition",I(a,"food")>0);
        Score("warm",.4+2.6*cold,"temperature",I(a,"wood")>0);
        Score("wash",.1+2.5*N(a,"hygiene"),"wash",I(a,"soap")>0&&I(a,"water")>0);
        Score("treat",2+a.Infection/25,"medical",I(a,"medicine")>0&&a.Infection>0);
        Score("bandage",2+a.Injury/25,"wound",I(a,"bandage")>0&&a.Injury>0);
        Score("boil",1.4+1.6*w,"water prep",I(a,"dirty_water")>0&&I(a,"wood")>0);
        Score("cook",1.2+1.4*h,"food prep",I(a,"raw_food")>0);
        Score("collect_water",1+2*w,"collect",
            Source(p,"dirty_water")&&I(a,"water")+I(a,"dirty_water")==0);
        Score("gather_food",1+1.8*h,"collect",
            Source(p,"raw_food")&&I(a,"food")+I(a,"raw_food")==0);
        Score("gather_wood",.55+1.1*cold+.4*Convert.ToInt32(I(a,"dirty_water")>0),
            "fuel",Source(p,"wood")&&I(a,"wood")==0);
        Score("build_shelter",.3+1.6*cold+1.2*N(a,"safety"),
            "build",I(a,"wood")>=3&&I(a,"shelter")==0);
        Score("build_latrine",.1+1.3*N(a,"hygiene")+.8*N(a,"bladder"),
            "build",I(a,"wood")>=2&&Depot(p,"latrine")==0);
        Score("take",1.8+2.3*w+.45*trait.Greed-.8*trait.Norm,
            "common supply",Depot(p,"water")>0&&w>.48,S+"water");
        Score("take",1.6+2.2*h+.45*trait.Greed-.75*trait.Norm,
            "common supply",Depot(p,"food")>0&&h>.53,S+"food");
        double margin=best-second;
        if(count<2||!double.IsFinite(margin)||margin<=1e-10)
            return null;
        var choice=new Decision(
            new ActionProposal(a.Id,S+winner,item),"deliberate",reason,count,best);
        return new ActionChoiceCertificate(choice,second,margin,
            "survival:exact-candidate-enumeration-v1");
    }
}
