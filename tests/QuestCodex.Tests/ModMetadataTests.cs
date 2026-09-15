namespace QuestCodex.Tests;

public class ModMetadataTests
{
    private readonly ModMetadata _meta = new();

    [Fact]
    public void Guid_and_author_use_viper_slug()
    {
        Assert.Equal("com.viper.questcodex", _meta.ModGuid);
        Assert.Equal("Viper-9", _meta.Author);
        Assert.Equal("QuestCodex", _meta.Name);
    }

    [Fact]
    public void Version_comes_from_assembly_informational_version()
    {
        Assert.Equal(ModMetadata.AssemblyVersion, _meta.Version.ToString());
        Assert.NotEqual("0.0.0", ModMetadata.AssemblyVersion);
    }

    [Fact]
    public void SptVersion_range_accepts_4_1_x()
    {
        Assert.True(_meta.SptVersion.IsSatisfied(new SemanticVersioning.Version("4.1.2")));
        Assert.True(_meta.SptVersion.IsSatisfied(new SemanticVersioning.Version("4.1.5")));
        Assert.False(_meta.SptVersion.IsSatisfied(new SemanticVersioning.Version("4.2.0")));
    }

    [Fact]
    public void Web_metadata_uses_lowercase_questcodex_prefix()
    {
        Assert.Equal("questcodex", _meta.WWWRootUrl);
        Assert.Equal("/questcodex", _meta.HomePage);
        Assert.False(string.IsNullOrWhiteSpace(_meta.HomePageDescription));
    }
}
