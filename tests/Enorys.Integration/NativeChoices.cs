using System.Diagnostics;
using System.Runtime.InteropServices;
using Enorys.Abstractions;

internal sealed class ManagedSpanChoice : IChoiceKernel {
    public string Name=>"C# span";
    public int ArgMax(ReadOnlySpan<double> values){
        if(values.Length==0||values.Length>1024) return -1;
        int winner=0;double best=values[0];
        if(!double.IsFinite(best))return -1;
        for(int i=1;i<values.Length;i++){
            if(!double.IsFinite(values[i]))return -1;
            if(values[i]>best){best=values[i];winner=i;}
        }
        return winner;
    }
}
internal sealed unsafe class NativeChoice(string name) : IChoiceKernel {
    public string Name=>name;
    [DllImport("enorys_choice_c",EntryPoint="enorys_argmax_c",CallingConvention=CallingConvention.Cdecl)]
    private static extern int CChoice(double* values,int length);
    [DllImport("enorys_choice_zig",EntryPoint="enorys_argmax_zig",CallingConvention=CallingConvention.Cdecl)]
    private static extern int ZigChoice(double* values,int length);
    [DllImport("enorys_choice_go",EntryPoint="enorys_argmax_go",CallingConvention=CallingConvention.Cdecl)]
    private static extern int GoChoice(double* values,int length);
    public int ArgMax(ReadOnlySpan<double> values) {
        fixed(double* p=values){
            return name switch {
                "C"=>CChoice(p,values.Length),"Zig"=>ZigChoice(p,values.Length),
                "Go"=>GoChoice(p,values.Length),_=>throw new ArgumentOutOfRangeException(nameof(name))
            };
        }
    }
}

internal sealed class TimedWorld(IWorldAdapter inner) : IWorldAdapter,IObservationSignal {
    public long ViewTicks,ObservationTicks,ExecutionTicks,AdvanceTicks,AliveTicks;
    public int AgentCount=>inner.AgentCount;
    public int TotalTurns=>inner.TotalTurns;
    public long Tick=>inner.Tick;
    public AgentView GetPrivateAgent(int index){
        long now=Stopwatch.GetTimestamp();
        var result=inner.GetPrivateAgent(index);
        ViewTicks+=Stopwatch.GetTimestamp()-now;
        return result;
    }
    public ObservationPacket Observe(int index){
        long now=Stopwatch.GetTimestamp();
        var result=inner.Observe(index);
        ObservationTicks+=Stopwatch.GetTimestamp()-now;
        return result;
    }
    public ActionResult Execute(ActionProposal p){
        long now=Stopwatch.GetTimestamp();
        var result=inner.Execute(p);
        ExecutionTicks+=Stopwatch.GetTimestamp()-now;
        return result;
    }
    public bool IsAlive(int index){
        long now=Stopwatch.GetTimestamp();
        var result=inner.IsAlive(index);
        AliveTicks+=Stopwatch.GetTimestamp()-now;
        return result;
    }
    public void Advance(){
        long now=Stopwatch.GetTimestamp();
        inner.Advance();
        AdvanceTicks+=Stopwatch.GetTimestamp()-now;
    }
    public bool HasPendingObservation(int index)=>
        inner is IObservationSignal signal && signal.HasPendingObservation(index);
    public WorldMetrics GetMetrics()=>inner.GetMetrics();
}

internal sealed class TimedChoice(IChoiceKernel inner) : IChoiceKernel {
    public string Name=>"Timed "+inner.Name;
    public long Ticks {get;private set;}
    public long Calls {get;private set;}
    public int ArgMax(ReadOnlySpan<double> values){
        long start=Stopwatch.GetTimestamp();
        int index=inner.ArgMax(values);
        Ticks+=Stopwatch.GetTimestamp()-start;
        Calls++;
        return index;
    }
}
