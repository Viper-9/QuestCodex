using Microsoft.AspNetCore.Mvc;
using QuestCodex.Web.Controllers;

namespace QuestCodex.Tests;

public class PingControllerTests
{
    [Fact]
    public void Ping_returns_mod_name_and_versions()
    {
        var result = new PingController().Ping();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<PingResponse>(ok.Value);
        Assert.Equal("QuestCodex", body.Mod);
        Assert.Equal(ModMetadata.AssemblyVersion, body.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+", body.Spt);
    }
}
