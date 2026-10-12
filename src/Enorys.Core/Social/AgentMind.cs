using Enorys.Abstractions;
using Enorys.Core.Planning;
namespace Enorys.Core.Social;

/// <summary>Agent-private state; never holds a reference to the authoritative world.</summary>
public sealed class AgentMind {
    public Plan? Plan;
    public readonly Dictionary<string,PrivateEvidence> Evidence=new(StringComparer.Ordinal);
    public readonly Dictionary<string,double> Trust=new(StringComparer.Ordinal);
    public readonly Dictionary<string,int> Debts=new(StringComparer.Ordinal);
    public readonly List<HelpRequest> Requests=new();
    public readonly HashSet<string> Acted=new(StringComparer.Ordinal);
    public readonly HashSet<string> Reported=new(StringComparer.Ordinal);
    public readonly List<(string Root,string To)> SentReports=new();
    public readonly List<(string Root,string To)> Corrections=new();
    public long LastInboxTick=-1;

    public void Ingest(ObservationPacket packet){
        if(packet.Messages.Count>0)LastInboxTick=packet.Tick;
        foreach(var m in packet.Messages){
            switch(m.Type){
                case "evidence":
                    if(m.Root==null||m.Incident==null||m.Suspect==null||!double.IsFinite(m.LogLikelihood)||Evidence.ContainsKey(m.Root))break;
                    double trust=Trust.GetValueOrDefault(m.From);
                    double reliability=m.Source=="eyewitness"?1:m.Source=="trace"?.85:
                        Math.Clamp(.58+trust/250,.2,.92);
                    Evidence.Add(m.Root,new PrivateEvidence(m.Root,m.Incident,m.Suspect,
                        m.LogLikelihood*reliability,m.Source??"hearsay",m.From,m.Tick));
                    if(m.Source is "trace" or "eyewitness"){
                        foreach(var sent in SentReports){
                            if(Evidence.TryGetValue(sent.Root,out var old)&&old.Incident==m.Incident&&old.Suspect!=m.Suspect&&
                               !Corrections.Contains(sent))Corrections.Add(sent);
                        }
                    }
                    break;
                case "retract":
                    if(m.Root!=null&&Evidence.TryGetValue(m.Root,out var record)&&record.Sender==m.From&&record.Active){
                        Evidence[m.Root]=record with { Active=false };
                        Trust[m.From]=Math.Clamp(Trust.GetValueOrDefault(m.From)-4,-100,100);
                    }
                    break;
                case "gift":
                    if(m.Item!=null){string key=m.From+":"+m.Item;Debts[key]=Debts.GetValueOrDefault(key)+1;Trust[m.From]=Math.Clamp(Trust.GetValueOrDefault(m.From)+8,-100,100);}
                    break;
                case "repayment":Trust[m.From]=Math.Clamp(Trust.GetValueOrDefault(m.From)+4,-100,100);break;
                case "accused":Trust[m.From]=Math.Clamp(Trust.GetValueOrDefault(m.From)-6,-100,100);break;
                case "request":
                    if(m.Item!=null){Requests.Add(new HelpRequest(m.From,m.Item,m.Tick));if(Requests.Count>30)Requests.RemoveAt(0);}
                    break;
            }
        }
    }

    public Suspicion? Strongest(long tick){
        var records=Evidence.Values.Where(e=>e.Active).GroupBy(e=>e.Incident+":"+e.Suspect);
        Suspicion? best=null;
        foreach(var group in records){
            var ordered=group.OrderByDescending(e=>e.LogLikelihood).ThenBy(e=>e.Root,StringComparer.Ordinal).ToList();
            double logit=Math.Log(.12/.88);
            for(int i=0;i<ordered.Count;i++)
                logit+=ordered[i].LogLikelihood*Math.Exp(-Math.Max(0,tick-ordered[i].Tick)/800.0)*(i==0?1:.25);
            double probability=1/(1+Math.Exp(-logit));
            if(best==null||probability>best.Probability)
                best=new Suspicion(ordered[0].Incident,ordered[0].Suspect,probability,ordered[0].Root,ordered[0].LogLikelihood);
        }
        return best;
    }
    public void OnAccepted(ActionProposal p,int day,long tick){
        switch(p.Kind){
            case "survival:ask":Acted.Add("ask:"+p.Item+":"+day);break;
            case "survival:investigate":Acted.Add("investigate:"+p.Incident);break;
            case "survival:accuse":Acted.Add("accuse:"+p.Incident+":"+p.Target);break;
            case "survival:tell":
                if(p.Root!=null&&p.Target!=null){Reported.Add(p.Root+":"+p.Target);SentReports.Add((p.Root,p.Target));}break;
            case "survival:retract":
                Reported.Add("retract:"+p.Root+":"+p.Target);
                Corrections.RemoveAll(x=>x.Root==p.Root&&x.To==p.Target);break;
            case "survival:repay":
                if(p.Target!=null&&p.Item!=null){
                    string key=p.Target+":"+p.Item;Debts[key]=Math.Max(0,Debts.GetValueOrDefault(key)-1);
                }break;
            case "survival:give":
                Requests.RemoveAll(x=>x.From==p.Target&&x.Item==p.Item);break;
        }
    }
}

public sealed record Suspicion(string Incident,string Suspect,double Probability,string Root,double LogLikelihood);
