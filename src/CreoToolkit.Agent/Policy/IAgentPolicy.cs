using CreoToolkit.Agent.Commands;

namespace CreoToolkit.Agent.Policy;

public interface IAgentPolicy
{
    PolicyDecision Evaluate(AgentCommand command);
}
