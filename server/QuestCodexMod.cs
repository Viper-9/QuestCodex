using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace QuestCodex;

[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public class QuestCodexMod(ISptLogger<QuestCodexMod> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        logger.Success($"[QuestCodex] v{ModMetadata.AssemblyVersion} loaded. Web UI: /questcodex");
        return Task.CompletedTask;
    }
}
