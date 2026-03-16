using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace STS2AIAgent.Agent;

internal sealed partial class AiServicePumpNode : Node
{
    private const string LogPrefix = "[STS2AIAgent.Pump]";
    private const double TickIntervalSeconds = 1.0d;
    public const string NodeName = "STS2AIAgentServicePump";

    private double _elapsedSeconds;

    public override void _Ready()
    {
        Name = NodeName;
        ProcessMode = ProcessModeEnum.Always;
        SetProcess(true);
        Log.Info($"{LogPrefix} Service pump ready with 1s polling enabled.");
    }

    public override void _Process(double delta)
    {
        _elapsedSeconds += delta;
        if (_elapsedSeconds < TickIntervalSeconds)
        {
            return;
        }

        _elapsedSeconds = 0d;
        AiAgentService.Instance.Tick();
    }

    public static void EnsureMounted()
    {
        var tree = Engine.GetMainLoop() as SceneTree;
        var root = tree?.Root;
        if (root == null || !GodotObject.IsInstanceValid(root))
        {
            return;
        }

        var existing = root.GetNodeOrNull<AiServicePumpNode>(NodeName);
        if (existing != null && GodotObject.IsInstanceValid(existing))
        {
            return;
        }

        root.AddChild(new AiServicePumpNode());
    }
}
