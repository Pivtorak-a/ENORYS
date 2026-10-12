using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Enorys.Core.Epistemics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct NativeCause(double prior, double strength, double likelihoodRatio) {
    public readonly double Prior = prior, Strength = strength, LikelihoodRatio = likelihoodRatio;
}

internal static unsafe class Native {
    [DllImport("enorys_math", EntryPoint="enorys_noisy_or", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int Solve(NativeCause* causes, int count, double leak,
        double* output, double* diagnostics);
    [DllImport("enorys_math", EntryPoint="enorys_noisy_or_batch", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int Batch(NativeCause* causes, int count, double* leaks, int length,
        double* output, double* diagnostics);
}

internal static unsafe class Program {
    static double checkSink;

    private static void Near(double actual,double expected,double tolerance=1e-11) {
        if(!double.IsFinite(actual)||!double.IsFinite(expected)||
           Math.Abs(actual-expected)>tolerance)
            throw new InvalidOperationException($"Mismatch: C={actual:R}, C#={expected:R}, diff={actual-expected:R}");
    }

    // Same enumeration and log-sum-exp model as the native library and canonical C#.
    // Inputs and output buffers are supplied by the caller to avoid per-call allocation.
    private static void ManagedOptimized(NativeCause* causes,int n,double leak,double* output,double* diagnostics) {
        Span<double> weights=stackalloc double[256];
        Span<double> logpriors=stackalloc double[256];
        double max=double.NegativeInfinity;
        int states=1<<n;
        for(int mask=0;mask<states;mask++){
            double prior=0,fail=1-leak;
            bool possible=true;
            for(int i=0;i<n;i++){
                var c=causes[i];
                bool on=(mask&(1<<i))!=0;
                double p=on?c.Prior:1-c.Prior;
                if(p==0){possible=false;break;}
                prior+=Math.Log(p);
                if(on){prior+=Math.Log(c.LikelihoodRatio);fail*=1-c.Strength;}
            }
            logpriors[mask]=possible?prior:double.NegativeInfinity;
            double effect=1-fail;
            weights[mask]=possible&&effect>0
                ? prior+Math.Log(effect):double.NegativeInfinity;
            max=Math.Max(max,weights[mask]);
        }
        if(double.IsNegativeInfinity(max))throw new InvalidOperationException("Impossible event");
        double sum=0,empty=0,leakMass=0;
        for(int i=0;i<n;i++)output[i]=0;
        for(int mask=0;mask<states;mask++){
            double scaled=double.IsNegativeInfinity(weights[mask])?0:Math.Exp(weights[mask]-max);
            sum+=scaled;
            if(mask==0)empty+=scaled;
            for(int i=0;i<n;i++)if((mask&(1<<i))!=0)output[i]+=scaled;
            if(leak>0&&!double.IsNegativeInfinity(logpriors[mask]))
                leakMass+=Math.Exp(logpriors[mask]+Math.Log(leak)-max);
        }
        for(int i=0;i<n;i++)output[i]/=sum;
        diagnostics[0]=empty/sum;
        diagnostics[1]=leakMass/sum;
        diagnostics[2]=max+Math.Log(sum);
    }

    private sealed class Data {
        public required NativeCause[] Native;
        public required Cause[][] Managed;
        public required double[] Leaks;
        public required double[] Output;
        public required double[] Diagnostics;
    }

    private static Data Prepare(int n,int batch) {
        var random=new Random(20261012);
        var flat=new NativeCause[n*batch];var arrays=new Cause[batch][];
        var leaks=new double[batch];
        for(int i=0;i<batch;i++) {
            leaks[i]=.01+random.NextDouble()*.2;
            arrays[i]=new Cause[n];
            for(int j=0;j<n;j++){
                double p=.001+random.NextDouble()*.998;
                double s=.001+random.NextDouble()*.998;
                double lr=.1+random.NextDouble()*4;
                arrays[i][j]=new Cause(p,s,lr);
                flat[i*n+j]=new NativeCause(p,s,lr);
            }
        }
        return new Data {Native=flat,Managed=arrays,Leaks=leaks,
            Output=new double[n*batch],Diagnostics=new double[batch*3]};
    }

    private static void Validate(Data d,int n) {
        fixed(NativeCause* c=d.Native)
        fixed(double* leaks=d.Leaks)
        fixed(double* output=d.Output)
        fixed(double* diagnostics=d.Diagnostics){
            if(Native.Batch(c,n,leaks,d.Leaks.Length,output,diagnostics)!=0)
                throw new InvalidOperationException("Native batch failed");
            double* optimized=stackalloc double[8];
            double* extra=stackalloc double[3];
            for(int i=0;i<d.Leaks.Length;i++){
                var canonical=NoisyOr.Solve(d.Managed[i],d.Leaks[i]);
                for(int j=0;j<n;j++)Near(output[i*n+j],canonical.Causes[j]);
                Near(diagnostics[i*3],canonical.NoListedCause);
                Near(diagnostics[i*3+1],canonical.LeakActivated);
                Near(diagnostics[i*3+2],canonical.EvidenceLogNormalizer);
                if(Native.Solve(c+i*n,n,d.Leaks[i],optimized,extra)!=0)
                    throw new Exception("Native scalar rejected valid input");
                ManagedOptimized(c+i*n,n,d.Leaks[i],optimized,extra);
                for(int j=0;j<n;j++)Near(optimized[j],canonical.Causes[j]);
                Near(extra[0],canonical.NoListedCause);
            }
        }
    }

    private static double Compute(Data d,int n,string mode) {
        int count=d.Leaks.Length;
        fixed(NativeCause* c=d.Native)
        fixed(double* leaks=d.Leaks)
        fixed(double* output=d.Output)
        fixed(double* diag=d.Diagnostics){
            switch(mode){
                case "C# canonical":
                    for(int i=0;i<count;i++){
                        var r=NoisyOr.Solve(d.Managed[i],leaks[i]);
                        for(int j=0;j<n;j++)output[i*n+j]=r.Causes[j];
                        diag[i*3]=r.NoListedCause;diag[i*3+1]=r.LeakActivated;diag[i*3+2]=r.EvidenceLogNormalizer;
                    }break;
                case "C# optimized":
                    for(int i=0;i<count;i++)
                        ManagedOptimized(c+i*n,n,leaks[i],output+i*n,diag+i*3);
                    break;
                case "C scalar P/Invoke":
                    for(int i=0;i<count;i++)
                        if(Native.Solve(c+i*n,n,leaks[i],output+i*n,diag+i*3)!=0)
                            throw new Exception("Native scalar failed");
                    break;
                case "C batch P/Invoke":
                    if(Native.Batch(c,n,leaks,count,output,diag)!=0)
                        throw new Exception("Native batch failed");
                    break;
                default:throw new Exception("Unknown mode");
            }
        }
        double checksum=0;
        for(int i=0;i<count;i++) checksum+=d.Output[i*n]+d.Diagnostics[i*3+1]*.01;
        checkSink=checksum;
        return checksum;
    }

    private static void Benchmark(int n,int operations) {
        Data d=Prepare(n,operations);
        Validate(Prepare(n, Math.Min(500,operations)),n);
        var modes=new[]{"C# canonical","C# optimized","C scalar P/Invoke","C batch P/Invoke"};
        var outputs=new List<object>();
        foreach(string mode in modes){
            Compute(d,n,mode); // JIT and native function warmup
            var times=new List<double>();
            var allocs=new List<double>();
            var checksums=new List<double>();
            for(int attempt=0;attempt<5;attempt++){
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                long before=GC.GetTotalAllocatedBytes(true);
                var clock=Stopwatch.StartNew();
                double result=Compute(d,n,mode);
                clock.Stop();
                times.Add(clock.Elapsed.TotalMilliseconds);
                allocs.Add((double)(GC.GetTotalAllocatedBytes(true)-before)/operations);
                checksums.Add(result);
            }
            times.Sort();
            for(int i=1;i<checksums.Count;i++)Near(checksums[i],checksums[0]);
            var entry=new{mode,causes=n,operations,medianMs=times[2],
                minMs=times[0],maxMs=times[^1],
                throughputPerSecond=operations/(times[2]/1000),
                allocationBytesPerSolve=allocs.Average(),checksum=checksums[0]};
            outputs.Add(entry);
            Console.WriteLine("NATIVE_BENCH "+JsonSerializer.Serialize(entry));
        }
    }

    private static int Main(){
        Console.WriteLine("NATIVE_ENV "+JsonSerializer.Serialize(new {
            version=Environment.Version.ToString(),framework=RuntimeInformation.FrameworkDescription,
            os=RuntimeInformation.OSDescription,platform=RuntimeInformation.ProcessArchitecture.ToString(),
            cores=Environment.ProcessorCount,compiler="gcc -O3 -march=x86-64",repeats=5
        }));
        try{
            // Run many random inputs to verify mathematical correspondence before timing.
            Validate(Prepare(2,1000),2);
            Validate(Prepare(8,1000),8);
            Console.WriteLine("NATIVE_CORRECTNESS 2000 randomly generated independent scenarios passed");
            Benchmark(2,40000);
            Benchmark(8,2500);
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
