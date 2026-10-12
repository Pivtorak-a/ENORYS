namespace Enorys.Core.Runtime;

/// <summary>
/// Sparse calendar for background NPCs in a fixed authoritative actor-order
/// world. Active actor slots still run each round. Pending private observations
/// may invalidate a due date without consuming the observation.
/// </summary>
public interface IPulseActivationCalendar {
    long NextDueFrom(long currentTick);
    void AfterTurn(int actor,long completedTick,int rounds);
    void InvalidateFromPrivateMessage(int actor,long currentTick);
    bool ConsumeDue(int actor,long now);
    void Skipped(int slots);
    long SkippedSlots {get;}
    long Interrupted {get;}
    int HighWater {get;}
}
public sealed class PulseCalendar : IPulseActivationCalendar {
    private readonly record struct Wake(int Agent,int Version);
    private readonly PriorityQueue<Wake,long> queue=new();
    private readonly long[] due;
    private readonly int[] versions;
    private readonly int population,active;
    public long Interrupted {get;private set;}
    public long ScheduledWakeups {get;private set;}
    public long SkippedSlots {get;private set;}
    public int HighWater {get;private set;}
    public PulseCalendar(int population,int active){
        if(population<2||active<1||active>=population)
            throw new ArgumentOutOfRangeException(nameof(active));
        this.population=population;this.active=active;
        due=new long[population];versions=new int[population];
        Array.Fill(due,long.MaxValue);
        for(int actor=active;actor<population;actor++)Set(actor,actor);
    }
    private void Set(int agent,long deadline){
        due[agent]=deadline;
        int ver=++versions[agent];
        queue.Enqueue(new Wake(agent,ver),deadline);
        HighWater=Math.Max(HighWater,queue.Count);
    }
    public void AfterTurn(int actor,long completedTick,int rounds){
        if(actor<active||actor>=population||rounds<1||rounds>1000)
            throw new ArgumentOutOfRangeException(nameof(actor));
        Set(actor,checked(completedTick+(long)rounds*population));
    }
    public void InvalidateFromPrivateMessage(int actor,long currentTick){
        if(actor<active||actor>=population)return;
        int slot=(int)(currentTick%population);
        long offset=actor-slot;
        if(offset<=0)offset+=population;
        long earliest=currentTick+offset;
        if(earliest<due[actor]){
            Interrupted++;
            Set(actor,earliest);
        }
    }
    private void CullStale(){
        while(queue.TryPeek(out Wake wake,out long priority)){
            if(wake.Version==versions[wake.Agent]&&due[wake.Agent]==priority)break;
            queue.Dequeue();
        }
    }
    public long NextDueFrom(long currentTick){
        CullStale();return queue.TryPeek(out _,out long time)?time:long.MaxValue;
    }
    public bool ConsumeDue(int actor,long now){
        CullStale();
        if(!queue.TryPeek(out Wake wake,out long priority)||
           priority!=now||wake.Agent!=actor)return false;
        queue.Dequeue();
        due[actor]=long.MaxValue;
        ScheduledWakeups++;
        return true;
    }
    public void Skipped(int slots){
        if(slots<0)throw new ArgumentOutOfRangeException(nameof(slots));
        SkippedSlots+=slots;
    }
}


/// <summary>
/// O(1) wake/reschedule per actor, O(background slots) scan per round.
/// Uses actor slot alignment to avoid priority queue allocation. Best when
/// a moderate active cohort forces a fixed world round anyway.
/// </summary>
public sealed class PulseWheelCalendar : IPulseActivationCalendar {
    private readonly int population,active;
    private readonly long[] due;
    public long Interrupted {get;private set;}
    public long SkippedSlots {get;private set;}
    public int HighWater=>0; // No heap exists.
    public PulseWheelCalendar(int population,int active){
        if(population<2||active<1||active>=population)
            throw new ArgumentOutOfRangeException(nameof(active));
        this.population=population;this.active=active;
        due=new long[population];
        Array.Fill(due,long.MaxValue);
        for(int i=active;i<population;i++)due[i]=i;
    }
    public long NextDueFrom(long currentTick){
        int slot=(int)(currentTick%population);
        long baseTick=currentTick-slot;
        for(int i=Math.Max(active,slot);i<population;i++){
            long target=baseTick+i;
            if(due[i]<=target)return target;
        }
        return long.MaxValue;
    }
    public void AfterTurn(int actor,long completedTick,int rounds){
        if(actor<active||actor>=population||rounds<1||rounds>1000)
            throw new ArgumentOutOfRangeException(nameof(actor));
        due[actor]=checked(completedTick+(long)rounds*population);
    }
    public void InvalidateFromPrivateMessage(int actor,long currentTick){
        if(actor<active||actor>=population)return;
        int slot=(int)(currentTick%population);
        long offset=actor-slot;
        if(offset<=0)offset+=population;
        long target=currentTick+offset;
        if(target<due[actor]){due[actor]=target;Interrupted++;}
    }
    public bool ConsumeDue(int actor,long now){
        if(actor<active||actor>=population||due[actor]>now)return false;
        due[actor]=long.MaxValue;
        return true;
    }
    public void Skipped(int slots){
        if(slots<0)throw new ArgumentOutOfRangeException(nameof(slots));
        SkippedSlots+=slots;
    }
}
