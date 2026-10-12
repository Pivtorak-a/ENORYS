namespace Enorys.PulseGraph;

/// <summary>Incremental integer-valued cognitive signals. This is an
/// isolated scheduler proof, not the entire existing ENORYS agent runtime.</summary>
public sealed class SignalWorld {
    private const int Maximum=2000;
    private readonly SignalDefinition[] definitions;
    private readonly int[] level,updated,perAction;
    private readonly List<string> firstActions=new(12);
    private ulong hash=14695981039346656037UL;
    public int AgentCount {get;}
    public int Dimensions=>definitions.Length;
    public long Checks {get;private set;}
    public long Materializations {get;private set;}
    public long ActionCount {get;private set;}
    public ulong Hash=>hash;
    public IReadOnlyList<string> FirstActions=>firstActions;
    public int[] ActionCounts=>perAction.ToArray();

    public SignalWorld(int agents,SignalDefinition[] definitions,int seed){
        if(agents<1||agents>100000||definitions.Length<1||definitions.Length>32)
            throw new ArgumentOutOfRangeException(nameof(agents));
        AgentCount=agents;this.definitions=definitions;
        level=new int[agents*definitions.Length];updated=new int[agents];
        perAction=new int[definitions.Length];
        var random=new Random(seed);
        for(int i=0;i<level.Length;i++)level[i]=random.Next(100,451);
    }
    private void AdvanceActor(int actor,int tick){
        int elapsed=tick-updated[actor];
        if(elapsed<0)throw new InvalidOperationException("Time moved backward");
        if(elapsed==0)return;
        int offset=actor*Dimensions;
        for(int k=0;k<Dimensions;k++){
            level[offset+k]=Math.Min(Maximum,level[offset+k]+definitions[k].Rate*elapsed);
            Materializations++;
        }
        updated[actor]=tick;
    }
    public void OnStimulus(Stimulus stimulus){
        AdvanceActor(stimulus.Agent,stimulus.Tick);
        int index=stimulus.Agent*Dimensions+stimulus.Signal;
        level[index]=Math.Clamp(level[index]+stimulus.Delta,0,Maximum);
    }
    public int NextDue(int actor,int now,int earliest,int endTick){
        if(earliest<=now)throw new ArgumentOutOfRangeException(nameof(earliest));
        int elapsed=now-updated[actor];
        int offset=actor*Dimensions;
        int soon=int.MaxValue;
        for(int k=0;k<Dimensions;k++){
            Checks++;
            int current=Math.Min(Maximum,level[offset+k]+definitions[k].Rate*elapsed);
            int due=current>=definitions[k].Threshold
                ?earliest
                :now+(definitions[k].Threshold-current+definitions[k].Rate-1)/definitions[k].Rate;
            if(due<earliest)due=earliest;
            if(due<soon)soon=due;
        }
        return soon<=endTick?soon:int.MaxValue;
    }
    public bool React(int actor,int tick){
        AdvanceActor(actor,tick);
        int offset=actor*Dimensions,chosen=-1,best=int.MinValue;
        for(int k=0;k<Dimensions;k++){
            Checks++;
            int urgency=level[offset+k]-definitions[k].Threshold;
            if(urgency>=0 && urgency>best){chosen=k;best=urgency;}
        }
        if(chosen<0)return false;
        var rule=definitions[chosen];
        level[offset+chosen]=Math.Max(0,level[offset+chosen]-rule.Relief);
        int coupled=(chosen+1)%Dimensions;
        level[offset+coupled]=Math.Min(Maximum,level[offset+coupled]+rule.CoupledDelta);
        perAction[chosen]++;ActionCount++;
        unchecked {
            hash=(hash^(uint)tick)*1099511628211UL;
            hash=(hash^(uint)actor)*1099511628211UL;
            hash=(hash^(uint)chosen)*1099511628211UL;
        }
        if(firstActions.Count<12)firstActions.Add(
            $"tick={tick} agent={actor} action=regulate:{rule.Id}");
        return true;
    }
}
public sealed record RunResult(string Algorithm,int Agents,int Ticks,int Signals,int Stimuli,
    long Actions,ulong TraceHash,long Checks,long Materializations,
    long SkippedSlots,int[] ActionCounts,string[] FirstActions);

public static class FullTickScan {
    public static RunResult Run(int agents,int ticks,SignalDefinition[] rules,
        Stimulus[] events,int seed) {
        var world=new SignalWorld(agents,rules,seed);
        int next=0;
        for(int tick=1;tick<=ticks;tick++){
            while(next<events.Length && events[next].Tick==tick)
                world.OnStimulus(events[next++]);
            for(int agent=0;agent<agents;agent++)world.React(agent,tick);
        }
        return new RunResult("scan",agents,ticks,rules.Length,events.Length,
            world.ActionCount,world.Hash,world.Checks,world.Materializations,0,
            world.ActionCounts,world.FirstActions.ToArray());
    }
}
public static class SparsePulseScheduler {
    private readonly record struct Wake(int Agent,int Version);
    public static RunResult Run(int agents,int ticks,SignalDefinition[] rules,
        Stimulus[] events,int seed){
        var world=new SignalWorld(agents,rules,seed);
        var versions=new int[agents];
        var priorityQueue=new PriorityQueue<Wake,long>();
        var touched=new int[agents];
        Array.Fill(touched,-1);
        void NaturalWake(int agent,int now,int earliest){
            int version=++versions[agent];
            int time=world.NextDue(agent,now,earliest,ticks);
            if(time==int.MaxValue)return;
            priorityQueue.Enqueue(new Wake(agent,version),
                (long)time*(agents+1)+agent);
        }
        for(int actor=0;actor<agents;actor++)NaturalWake(actor,0,1);
        void Drain(int time,bool inclusive){
            while(priorityQueue.TryPeek(out _,out long next)){
                int due=(int)(next/(agents+1));
                if(inclusive?due>time:due>=time)break;
                priorityQueue.TryDequeue(out Wake wake,out _);
                if(wake.Version!=versions[wake.Agent])continue;
                world.React(wake.Agent,due);
                NaturalWake(wake.Agent,due,due+1);
            }
        }
        int cursor=0;
        while(cursor<events.Length){
            int time=events[cursor].Tick;
            Drain(time,inclusive:false);
            var changed=new List<int>();
            while(cursor<events.Length && events[cursor].Tick==time){
                var evt=events[cursor++];
                world.OnStimulus(evt);
                if(touched[evt.Agent]!=time){
                    touched[evt.Agent]=time;
                    changed.Add(evt.Agent);
                }
            }
            // External observations arrive before choices at the same tick.
            // Invalidate any prior wake and force one immediate private update.
            foreach(int actor in changed){
                int version=++versions[actor];
                priorityQueue.Enqueue(new Wake(actor,version),
                    (long)time*(agents+1)+actor);
            }
            Drain(time,inclusive:true);
        }
        Drain(ticks,inclusive:true);
        return new RunResult("sparse",agents,ticks,rules.Length,events.Length,
            world.ActionCount,world.Hash,world.Checks,world.Materializations,
            (long)agents*ticks-world.ActionCount,
            world.ActionCounts,world.FirstActions.ToArray());
    }
}
