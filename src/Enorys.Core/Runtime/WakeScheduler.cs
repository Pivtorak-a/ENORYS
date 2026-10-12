namespace Enorys.Core.Runtime;

/// <summary>
/// Event-aware turn scheduler. The world still advances its authoritative clock
/// each turn; a sleeping agent skips cognition until its due slot, unless private
/// incoming observations require early wake. This is a hybrid, not a full
/// discrete-event world clock.
/// </summary>
public sealed class WakeScheduler {
    private readonly long[] due;
    public long Skipped {get;private set;}
    public long Interrupted {get;private set;}
    public WakeScheduler(int agents){
        if(agents<=0)throw new ArgumentOutOfRangeException(nameof(agents));
        due=new long[agents];
    }
    public bool ShouldRun(int actorIndex,long tick,bool pendingObservation){
        if(actorIndex<0||actorIndex>=due.Length||tick<0)throw new ArgumentOutOfRangeException();
        if(pendingObservation && tick<due[actorIndex]){Interrupted++;due[actorIndex]=tick;}
        if(tick>=due[actorIndex])return true;
        Skipped++;return false;
    }
    public void Schedule(int actorIndex,long tick,int sleepTurns){
        if(sleepTurns<0||sleepTurns>100)throw new ArgumentOutOfRangeException(nameof(sleepTurns));
        due[actorIndex]=checked(tick+(long)due.Length*(sleepTurns+1));
    }
    public long DueTick(int index)=>due[index];
}
