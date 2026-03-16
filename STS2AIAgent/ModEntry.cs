using System.Threading;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2AIAgent.Agent;
using STS2AIAgent.Desktop;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string LogPrefix = "[STS2AIAgent]";

    private static int _shutdownHooksRegistered;

    public static void Initialize()
    {
        Log.Info($"{LogPrefix} Initializing");
        RegisterShutdownHooks();
        GameThread.Initialize();
        AiAgentService.Instance.Initialize();
        _ = GameThread.InvokeAsync(() =>
        {
            AiServicePumpNode.EnsureMounted();
            return 0;
        });
        HttpServer.Instance.Start();
        DesktopWindowLauncher.TryLaunch();
        Log.Info($"{LogPrefix} Ready");
    }

    private static void RegisterShutdownHooks()
    {
        if (Interlocked.Exchange(ref _shutdownHooksRegistered, 1) != 0)
        {
            return;
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown();
        AppDomain.CurrentDomain.DomainUnload += (_, _) => Shutdown();
    }

    private static void Shutdown()
    {
        try
        {
            AiAgentService.Instance.Shutdown();
            HttpServer.Instance.Stop();
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} Failed during shutdown: {ex}");
        }
    }
}
