using System.Reflection;
using System.Runtime.Loader;
using Enorys.Abstractions;
namespace Enorys.Core.Modules;
/// <summary>Trusted modules only. .NET AssemblyLoadContext is NOT a security sandbox.</summary>
public sealed class ModuleHost : IModuleRegistry {
    private readonly Dictionary<string, IDecisionModule> _decisions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAppraisalModule> _appraisals = new(StringComparer.Ordinal);
    private readonly List<IWakePolicy> _wakePolicies=new();
    private IBackgroundDeadlinePolicy? _backgroundDeadline;
    public IBackgroundDeadlinePolicy? BackgroundDeadline=>_backgroundDeadline;
    public void AddBackgroundDeadline(IBackgroundDeadlinePolicy policy){
        if(_backgroundDeadline!=null)throw new InvalidOperationException("Only one background deadline policy may own an agent");
        _backgroundDeadline=policy;
    }
    private readonly List<IAffectiveModule> _affectiveModules=new();
    public IReadOnlyList<IAffectiveModule> AffectiveModules=>_affectiveModules;
    public void AddAffectiveModule(IAffectiveModule module){if(_affectiveModules.Any(x=>x.ModuleId==module.ModuleId))throw new InvalidOperationException("Duplicate affect module");_affectiveModules.Add(module);}
    public IReadOnlyList<IWakePolicy> WakePolicies=>_wakePolicies;
    public void AddWakePolicy(IWakePolicy policy){if(_wakePolicies.Count>0)throw new InvalidOperationException("One active wake policy per world");_wakePolicies.Add(policy);}
    public IEnumerable<IDecisionModule> Decisions => _decisions.Values.OrderBy(x=>x.ModuleId,StringComparer.Ordinal);
    public IEnumerable<IAppraisalModule> Appraisals => _appraisals.Values.OrderBy(x=>x.ModuleId,StringComparer.Ordinal);
    public void AddDecision(IDecisionModule m) {
        if(string.IsNullOrWhiteSpace(m.ModuleId)||!_decisions.TryAdd(m.ModuleId,m))
            throw new InvalidOperationException("Duplicate/invalid decision module ID");
    }
    public void AddAppraisal(IAppraisalModule m) {
        if(string.IsNullOrWhiteSpace(m.ModuleId)||!_appraisals.TryAdd(m.ModuleId,m))
            throw new InvalidOperationException("Duplicate/invalid appraisal module ID");
    }
    public void Install(IEnorysModule module) {if(string.IsNullOrWhiteSpace(module.Id))throw new ArgumentException("Invalid module ID");module.Register(this);}
    public void LoadTrustedAssembly(string path) {
        var context=new AssemblyLoadContext("enorys-trusted-"+Path.GetFileName(path),isCollectible:false);
        var assembly=context.LoadFromAssemblyPath(Path.GetFullPath(path));
        foreach(var type in assembly.GetExportedTypes().Where(t=>typeof(IEnorysModule).IsAssignableFrom(t)&&t.IsClass&&!t.IsAbstract).OrderBy(t=>t.FullName,StringComparer.Ordinal)){
            if(Activator.CreateInstance(type) is not IEnorysModule module)throw new InvalidOperationException("Module requires a public no-arg constructor");
            Install(module);
        }
    }
}
