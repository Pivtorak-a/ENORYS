using Enorys.Abstractions;
namespace Enorys.CustomBehavior;
/// <summary>Separate sample plugin, compiled without referencing ENORYS Core or the survival adapter.</summary>
public sealed class FocusBehavior : IEnorysModule, IDecisionModule {
    public string Id => "demo:focus-behavior";
    public string ModuleId=>Id;
    public void Register(IModuleRegistry registry)=>registry.AddDecision(this);
    public Decision Choose(AgentView actor,ObservationPacket packet,IDecisionContext context){
        if(actor.Needs.GetValueOrDefault("demo:focus")>10 &&
           actor.Inventory.GetValueOrDefault("survival:water")>0)
            return new Decision(new ActionProposal(actor.Id,"demo:drink-tea"),
                "plugin","restore custom focus need",1,1);
        return new Decision(new ActionProposal(actor.Id,"survival:wait"),
            "plugin","no custom need action possible",1,0);
    }
}
