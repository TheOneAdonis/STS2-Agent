using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2AIAgent.Game;
using STS2AIAgent.Server;

namespace STS2AIAgent;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private const string LogPrefix = "[STS2AIAgent]";

    public static void Initialize()
    {
        Log.Info($"{LogPrefix} Initializing");
        GameThread.Initialize();
        HttpServer.Instance.Start();
        Log.Info($"{LogPrefix} Ready");
    }
}
