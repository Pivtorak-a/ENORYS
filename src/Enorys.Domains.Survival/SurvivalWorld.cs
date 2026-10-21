using Enorys.Abstractions;
using System.Collections.ObjectModel;
using Enorys.Core.Domain;
using Enorys.Core.Runtime;

namespace Enorys.Domains.Survival;

public sealed record SurvivalConfig(int Seed=42,int Agents=24,int Days=14,int TurnsPerDay=3,
    double Resources=1,double Visibility=.35,double Misidentification=.08,double Cold=.5,bool Rumors=true,
    bool VerifiedAccusationFeedback=false);

internal sealed class Survivor {
    public required string Id;
    public int Zone,Group,Index;
    public bool Alive=true;
    public double Health=95,Injury,Infection;
    public required Traits Traits;
    public Dictionary<string,int> Inv=new(StringComparer.Ordinal);
    public AgentView? CachedView;
    public Dictionary<string,double> Needs=new(StringComparer.Ordinal);
    public HashSet<string> Acted=new(StringComparer.Ordinal);
}

public sealed class SurvivalWorld : IWorldAdapter, IObservationSignal, IWorldFastForward, IPrivateWakeFeed, IWorldScheduledStimuli {
    const string S="survival:";
    public readonly DomainRegistry Domain;
    public readonly SurvivalConfig Config;
    private readonly Survivor[] people;
    private readonly Dictionary<string,Survivor> byId;
    private readonly Dictionary<int,Survivor[]> zones;
    private readonly Dictionary<string,WorldEvent> incidents=new(StringComparer.Ordinal);
    private readonly SortedDictionary<long,List<(int Receiver,Message Message)>> scheduledInputs=new();
    public long NextExternalStimulusTick=>scheduledInputs.Count==0?long.MaxValue:scheduledInputs.First().Key;
    // Trusted scenario fixture hook. Does not expose World Authority to ordinary NPC modules.
    public void ScheduleObservation(long atTick,int receiver,Message observation){
        if(atTick<=tick||atTick>=TotalTurns||receiver<0||receiver>=people.Length)
            throw new ArgumentOutOfRangeException(nameof(atTick));
        if(!scheduledInputs.TryGetValue(atTick,out var list)){
            list=new List<(int,Message)>();
            scheduledInputs.Add(atTick,list);
        }
        list.Add((receiver,observation));
    }
    private readonly Queue<int> dirtyActors=new();
    private readonly HashSet<int> dirtySet=new();
    private readonly Dictionary<string,int> depot,sources,initial,consumed,converted;
    private IReadOnlyDictionary<string,int>? depotView;
    private IReadOnlyDictionary<string,bool>? sourcesView;
    private IReadOnlyList<string>[] peersView=[];
    private void InvalidatePublicViews(bool depotChanged=false,bool sourcesChanged=false,bool peerChanged=false){
        if(depotChanged)depotView=null;
        if(sourcesChanged)sourcesView=null;
        if(peerChanged)peersView=[];
    }
    private readonly List<WorldEvent> events=new();
    private readonly Dictionary<string,List<Message>> inbox=new(StringComparer.Ordinal);
    private readonly Dictionary<string,int> actions=new(StringComparer.Ordinal);
    private long tick;
    private int day=1,guards;
    private long rejected;
    public int FalseAccusations {get;private set;}
    public int AccurateAccusations {get;private set;}
    public int EyeWitnesses {get;private set;}
    public IReadOnlyList<WorldEvent> Events => events;
    public int AgentCount=>people.Length;
    public int TotalTurns=>people.Length*Config.Days*Config.TurnsPerDay;
    public long Tick=>tick;

    static int Quantity(Dictionary<string,int> d,string key)=>d.GetValueOrDefault(key);
    static double Need(Survivor a,string key)=>a.Needs.GetValueOrDefault(S+key);
    static double Limit(double v)=>Math.Clamp(v,0,100);
    public SurvivalWorld(DomainRegistry domain,SurvivalConfig config){
        if(config.Seed<0||config.Agents is <12 or >6000||config.Days is <1 or >100||
          config.TurnsPerDay is <1 or >10||config.Resources is <0 or >3||
          config.Visibility is <0 or >1||config.Misidentification is <0 or >1||config.Cold is <0 or >1)
            throw new ArgumentOutOfRangeException(nameof(config));
        Domain=domain;Config=config;
        foreach(string needed in new[]{"food","water","wood","raw_food","dirty_water","medicine","bandage","soap","coat","bed","shelter","latrine"})
            if(!domain.Items.ContainsKey(S+needed))throw new InvalidOperationException("Missing survival item "+needed);
        foreach(string needed in new[]{"hunger","thirst","fatigue","cold","hygiene","bladder","safety","belonging","morale"})
            if(!domain.Needs.ContainsKey(S+needed))throw new InvalidOperationException("Missing survival need "+needed);
        var rng=new Random(config.Seed);
        people=Enumerable.Range(0,config.Agents).Select(i=>{
            var p=new Survivor{Id=$"A{i+1:D3}",Index=i,
                // Large populations spread into smaller, interleaved zones.
                // Early (active) and later (background) NPCs can still meet.
                Zone=config.Agents>500?i%Math.Max(4,config.Agents/32):i%4,Group=i%3,
                Traits=new Traits(rng.NextDouble(),rng.NextDouble(),rng.NextDouble(),rng.NextDouble(),rng.NextDouble(),rng.NextDouble())};
            foreach(var item in Domain.Items.Keys)p.Inv[item]=0;
            foreach(var nd in Domain.Needs.Values)p.Needs[nd.Id]=Math.Floor(18+rng.NextDouble()*18);
            p.Inv[S+"food"]=1+Convert.ToInt32(rng.NextDouble()>.65);
            p.Inv[S+"water"]=1+Convert.ToInt32(rng.NextDouble()>.65);
            p.Inv[S+"wood"]=Convert.ToInt32(rng.NextDouble()>.5);
            p.Inv[S+"soap"]=Convert.ToInt32(rng.NextDouble()>.8);
            return p;
        }).ToArray();
        byId=people.ToDictionary(x=>x.Id,x=>x,StringComparer.Ordinal);
        zones=people.GroupBy(p=>p.Zone).ToDictionary(g=>g.Key,g=>g.ToArray());
        depot=Empty(); sources=Empty();
        void Seed(Dictionary<string,int> d,string k,double n)=>d[S+k]=(int)Math.Round(n*config.Resources,MidpointRounding.AwayFromZero);
        Seed(depot,"food",Math.Ceiling(config.Agents*.2));Seed(depot,"water",Math.Ceiling(config.Agents*.25));
        Seed(depot,"bandage",2);Seed(depot,"medicine",2);Seed(depot,"soap",2);Seed(depot,"latrine",1);
        Seed(sources,"dirty_water",config.Days*config.Agents*.72);
        Seed(sources,"raw_food",config.Days*config.Agents*.62);
        Seed(sources,"wood",config.Days*config.Agents*.9);
        Seed(sources,"medicine",config.Agents*.1); Seed(sources,"bandage",config.Agents*.2);
        Seed(sources,"soap",config.Agents*.3);Seed(sources,"coat",config.Agents*.25);
        Seed(sources,"bed",config.Agents*.2);
        Seed(sources,"blanket",config.Agents*.25);Seed(sources,"tool",config.Agents*.25);
        initial=Empty();consumed=Empty();converted=Empty();
        foreach(var k in Domain.Items.Keys)initial[k]=Count(k);
        inbox=people.ToDictionary(p=>p.Id,_=>new List<Message>(),StringComparer.Ordinal);
    }

    Dictionary<string,int> Empty()=>Domain.Items.Keys.ToDictionary(k=>k,_=>0,StringComparer.Ordinal);
    int Count(string k)=>Quantity(depot,k)+Quantity(sources,k)+people.Sum(p=>Quantity(p.Inv,k));
    double RandomKey(int actor,long clock,int channel){
        return CounterRandom.Sample((ulong)Config.Seed,(ulong)actor+1,(ulong)clock+1,(ulong)channel);
    }
    public bool IsAlive(int index)=>people[index].Alive;
    public bool HasPendingObservation(int index)=>inbox[people[index].Id].Count>0;
    public void DrainWakeSignals(List<int> destination){
        while(dirtyActors.Count>0){
            int agent=dirtyActors.Dequeue();
            dirtySet.Remove(agent);
            destination.Add(agent);
        }
    }
    public AgentView GetPrivateAgent(int index){
        var a=people[index];
        if(a.CachedView!=null)return a.CachedView;
        a.CachedView=new AgentView(a.Id,a.Zone,a.Group,a.Alive,
            new ReadOnlyDictionary<string,int>(new Dictionary<string,int>(a.Inv)),
            new ReadOnlyDictionary<string,double>(new Dictionary<string,double>(a.Needs)),
            a.Traits,a.Health,a.Injury,a.Infection,null,[],
            new ReadOnlyDictionary<string,double>(new Dictionary<string,double>()),
            new ReadOnlyDictionary<string,int>(new Dictionary<string,int>()),[],[],
            new HashSet<string>(),new HashSet<string>(),[]);
        return a.CachedView;
    }
    private void BuildPeers(){
        peersView=new IReadOnlyList<string>[people.Length];
        for(int i=0;i<people.Length;i++){
            var a=people[i];
            peersView[i]=Array.AsReadOnly(zones[a.Zone]
                .Where(p=>p.Alive&&p.Index!=a.Index).Select(p=>p.Id).ToArray());
        }
    }
    public ObservationPacket Observe(int index){
        var a=people[index];
        if(peersView.Length!=people.Length)BuildPeers();
        depotView??=new ReadOnlyDictionary<string,int>(new Dictionary<string,int>(depot));
        sourcesView??=new ReadOnlyDictionary<string,bool>(new Dictionary<string,bool>{
            [S+"raw_food"]=Quantity(sources,S+"raw_food")>0,
            [S+"dirty_water"]=Quantity(sources,S+"dirty_water")>0,
            [S+"wood"]=Quantity(sources,S+"wood")>0
        });
        var messages=inbox[a.Id].ToArray();inbox[a.Id].Clear();
        return new ObservationPacket(tick,day,Config.Cold,depotView,sourcesView,peersView[index],messages);
    }
    void Send(string? to,Message message){
        if(to!=null&&byId.TryGetValue(to,out var a)&&a.Alive){
            inbox[to].Add(message);
            if(dirtySet.Add(a.Index))dirtyActors.Enqueue(a.Index);
        }
    }
    public ActionResult Execute(ActionProposal p){
        if(p is null||!byId.TryGetValue(p.Actor,out var actor)||!actor.Alive)throw new ArgumentException("Invalid actor");
        var a=actor;string k=p.Kind.StartsWith(S,StringComparison.Ordinal)?p.Kind[S.Length..]:p.Kind;
        string item=p.Item??"";
        bool CanConsume(string id,int count=1)=>Quantity(a.Inv,S+id)>=count;
        bool HasSource(string id)=>Quantity(sources,S+id)>0;
        Survivor? target=p.Target!=null&&byId.TryGetValue(p.Target,out var t)?t:null;
        bool Neighbor()=>target!=null&&target.Alive&&target.Id!=a.Id&&target.Zone==a.Zone;
        bool supported=k switch {
            "wait" or "sleep" or "relieve" or "socialize" or "guard"=>true,
            "eat"=>CanConsume("food"),"drink"=>CanConsume("water"),
            "wash"=>CanConsume("soap")&&CanConsume("water"),"warm"=>CanConsume("wood"),
            "treat"=>CanConsume("medicine")&&a.Infection>0,
            "bandage"=>CanConsume("bandage")&&a.Injury>0,
            "gather_food"=>HasSource("raw_food"),"collect_water"=>HasSource("dirty_water"),
            "gather_wood"=>HasSource("wood"),
            "take"=>(item==S+"food"||item==S+"water"||item==S+"wood")&&Quantity(depot,item)>0,
            "give" or "repay"=>Neighbor()&&Domain.Items.ContainsKey(item)&&Quantity(a.Inv,item)>0,
            "ask"=>Neighbor()&&Domain.Items.ContainsKey(item),
            "tell" or "retract"=>Neighbor()&&p.Root!=null,
            "investigate"=>p.Incident!=null,"accuse"=>p.Incident!=null&&p.Target!=null,
            _=>Domain.Recipes.TryGetValue(p.Kind,out var recipe)&&recipe.Inputs.All(x=>Quantity(a.Inv,x.Key)>=x.Value)
               ||Domain.Actions.TryGetValue(p.Kind,out var custom)&&Quantity(a.Inv,custom.ItemId)>0
        };
        if(!supported){rejected++;return new ActionResult(false,"failed_precondition");}
        var evt=new WorldEvent(tick,day,a.Id,k,item.Length==0?null:item,p.Target,p.Incident,p.Root);
        void Spend(string i,int n=1){a.Inv[S+i]-=n;consumed[S+i]+=n;}
        void Restore(string name,double improvement){a.Needs[S+name]=Limit(Need(a,name)-improvement);}
        VerifiedIncidentOutcome? verifiedOutcome=null;
        switch(k){
            case "eat":Spend("food");Restore("hunger",57);break;
            case "drink":Spend("water");Restore("thirst",65);break;
            case "wash":Spend("soap");Spend("water");Restore("hygiene",55);break;
            case "warm":Spend("wood");Restore("cold",52);break;
            case "sleep":Restore("fatigue",CanConsume("bed")?63:48);break;
            case "relieve":Restore("bladder",70);if(Quantity(depot,S+"latrine")==0)a.Needs[S+"hygiene"]=Limit(Need(a,"hygiene")+12);break;
            case "treat":Spend("medicine");a.Infection=Limit(a.Infection-35);a.Health=Limit(a.Health+5);break;
            case "bandage":Spend("bandage");a.Injury=Limit(a.Injury-30);break;
            case "socialize":Restore("belonging",23);Restore("morale",12);break;
            case "gather_food":case "collect_water":case "gather_wood":
              string gathered=k=="gather_food"?"raw_food":k=="collect_water"?"dirty_water":"wood";
              sources[S+gathered]--;a.Inv[S+gathered]++;break;
            case "take":
              depot[item]--;a.Inv[item]++;evt=evt with {Incident=$"E{events.Count+1:D6}"};
              foreach(var witness in zones[a.Zone].Where(x=>x.Alive&&x.Id!=a.Id)){
                 int wi=witness.Index;
                 if(RandomKey(wi,tick,47)>=Config.Visibility)continue;
                 bool mistaken=RandomKey(wi,tick,71)<Config.Misidentification;
                 string suspect=mistaken?people.First(x=>x.Id!=a.Id&&x.Id!=witness.Id).Id:a.Id;
                 var root=evt.Incident+"/"+witness.Id;
                 Send(witness.Id,new Message("evidence",witness.Id,Incident:evt.Incident,Root:root,Suspect:suspect,LogLikelihood:Math.Log(8),Source:"eyewitness",Tick:tick));
                 EyeWitnesses++;
              }
              incidents[evt.Incident!]=evt;
              break;
            case "give":case "repay":
              a.Inv[item]--;target!.Inv[item]++;
              Send(target.Id,new Message(k=="give"?"gift":"repayment",a.Id,Item:item,Tick:tick));
              break;
            case "ask":Send(target!.Id,new Message("request",a.Id,Item:item,Tick:tick));break;
            case "tell":Send(target!.Id,new Message("evidence",a.Id,Incident:p.Incident,Root:p.Root,Suspect:p.Suspect,LogLikelihood:p.LogLikelihood*.6,Source:"hearsay",Tick:tick));break;
            case "retract":Send(target!.Id,new Message("retract",a.Id,Root:p.Root,Tick:tick));break;
            case "investigate":
              incidents.TryGetValue(p.Incident!,out var incident);
              if(incident!=null){
                  bool mistaken=RandomKey(a.Index,tick,77)<.20;
                  string suspect=mistaken?people[(a.Index+2)%people.Length].Id:incident.Actor;
                  Send(a.Id,new Message("evidence",a.Id,Incident:incident.Incident,Root:incident.Incident+"/clue/"+a.Id,Suspect:suspect,LogLikelihood:Math.Log(5),Source:"trace",Tick:tick));
              }
              a.Acted.Add("investigate:"+p.Incident);break;
            case "accuse":
              bool known=incidents.TryGetValue(p.Incident!,out var original);
              bool correct=known&&original!.Actor==p.Target;
              // Trusted experimental verdict for the ACTING NPC only. No
              // feedback for incident IDs outside authoritative world history.
              if(Config.VerifiedAccusationFeedback&&known)
                  verifiedOutcome=new VerifiedIncidentOutcome(p.Incident!,original!.Actor);
              if(correct)AccurateAccusations++;else FalseAccusations++;
              Send(target?.Id,new Message("accused",a.Id,Incident:p.Incident,Tick:tick));
              a.Acted.Add("accuse:"+p.Incident+":"+p.Target);break;
            case "guard":guards=(int)tick+people.Length;Restore("safety",12);break;
            default:
              if(Domain.Recipes.TryGetValue(p.Kind,out var rec)){
                  foreach(var x in rec.Inputs){a.Inv[x.Key]-=x.Value;converted[x.Key]-=x.Value;}
                  foreach(var x in rec.Outputs){a.Inv[x.Key]+=x.Value;converted[x.Key]+=x.Value;}
                  if(k=="build_latrine"){a.Inv[S+"latrine"]--;depot[S+"latrine"]++;}
                  if(k=="build_shelter")Restore("safety",20);
              } else if(Domain.Actions.TryGetValue(p.Kind,out var act)){
                  a.Inv[act.ItemId]--;consumed[act.ItemId]++;
                  a.Needs[act.NeedId]=Limit(a.Needs[act.NeedId]+act.NeedDelta);
              }
              break;
        }
        // Mutated views are rebuilt lazily; existing packets stay immutable.
        a.CachedView=null;
        if(k is "give" or "repay")target!.CachedView=null;
        if(k is "take" or "build_latrine")InvalidatePublicViews(depotChanged:true);
        if(k is "gather_food" or "collect_water" or "gather_wood" or "scavenge")InvalidatePublicViews(sourcesChanged:true);
        events.Add(evt);
        actions[k]=actions.GetValueOrDefault(k)+1;
        return new ActionResult(true,"accepted",evt){VerifiedOutcome=verifiedOutcome};
    }
    public void Advance()=>AdvanceSlots(1);
    public void AdvanceSlots(int slots){
        if(slots<0||tick+slots>TotalTurns)
            throw new ArgumentOutOfRangeException(nameof(slots));
        long target=tick+slots;
        long daySize=(long)people.Length*Config.TurnsPerDay;
        while(tick<target){
            long boundary=(tick/daySize+1)*daySize;
            long external=NextExternalStimulusTick;
            tick=Math.Min(target,Math.Min(boundary,external));
            if(tick==boundary){
                day++;
                bool anyDeath=false;
                foreach(var a in people.Where(p=>p.Alive)){
                    a.CachedView=null;
                    foreach(var n in Domain.Needs.Values)
                        a.Needs[n.Id]=Limit(a.Needs[n.Id]+n.Daily);
                    a.Needs[S+"cold"]=Limit(Need(a,"cold")+(Config.Cold>.7?11:4)-
                        (Can(a,"coat")?5:0)-(Can(a,"shelter")?7:0));
                    if(Need(a,"hunger")>85)a.Health-=5;
                    if(Need(a,"thirst")>85)a.Health-=9;
                    if(Need(a,"cold")>85)a.Health-=4;
                    if(a.Infection>0)a.Health-=a.Infection*.03;
                    if(Need(a,"hygiene")>85)a.Infection=Limit(a.Infection+3);
                    a.Health=Limit(a.Health);
                    if(a.Health<=0){a.Alive=false;anyDeath=true;}
                }
                if(anyDeath)InvalidatePublicViews(peerChanged:true);
            }
            if(tick==external&&scheduledInputs.Remove(tick,out var entries)){
                foreach(var entry in entries)
                    Send(people[entry.Receiver].Id,entry.Message with {Tick=tick});
            }
        }
    }
    static bool Can(Survivor a,string id)=>Quantity(a.Inv,S+id)>0;
    public bool ResourcesBalanced=>Domain.Items.Keys.All(k=>Count(k)-initial[k]-converted[k]+consumed[k]==0);
    public WorldMetrics GetMetrics()=>new(
        people.Average(p=>p.Health),people.Count(p=>p.Alive),(int)tick,0,0,0,rejected,0,
        events.Count,ResourcesBalanced,new Dictionary<string,int>(actions));
}
