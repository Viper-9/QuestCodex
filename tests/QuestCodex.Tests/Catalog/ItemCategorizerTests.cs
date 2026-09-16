using QuestCodex.Catalog.Rewards;
using SPTarkov.Server.Core.Models.Enums;
using static QuestCodex.Tests.Fixtures;

namespace QuestCodex.Tests.Catalog;

public class ItemCategorizerTests
{
    // 실제 부모 체인 흉내: M4A1 → ASSAULT_RIFLE → WEAPON → ITEM
    private static readonly ItemCategorizer Sut = new(Dict(
        (Id(1), Template(Id(1), BaseClasses.ASSAULT_RIFLE, "m4a1")),
        (BaseClasses.ASSAULT_RIFLE, Template(BaseClasses.ASSAULT_RIFLE, BaseClasses.WEAPON)),
        (BaseClasses.WEAPON, Template(BaseClasses.WEAPON, BaseClasses.ITEM)),
        (BaseClasses.ITEM, Template(BaseClasses.ITEM, default)),
        (Id(2), Template(Id(2), BaseClasses.ARMOR)),
        (BaseClasses.ARMOR, Template(BaseClasses.ARMOR, BaseClasses.ITEM)),
        (Id(3), Template(Id(3), BaseClasses.KEY_MECHANICAL)),
        (BaseClasses.KEY_MECHANICAL, Template(BaseClasses.KEY_MECHANICAL, BaseClasses.KEY)),
        (BaseClasses.KEY, Template(BaseClasses.KEY, BaseClasses.ITEM)),
        (Id(4), Template(Id(4), Id(99))),          // 부모가 테이블에 없음
        (Id(5), Template(Id(5), Id(5)))));         // 자기 참조 루프

    [Fact]
    public void Weapon_gets_major_and_sub_category()
        => Assert.Equal(["weapon", "assaultRifle"], Sut.Categorize(Id(1)));

    [Fact]
    public void Armor_gets_single_category()
        => Assert.Equal(["armor"], Sut.Categorize(Id(2)));

    [Fact]
    public void Key_subclass_resolves_through_parent_chain()
        => Assert.Equal(["key"], Sut.Categorize(Id(3)));

    [Fact]
    public void Unknown_tpl_returns_unknown()
        => Assert.Equal([ItemCategorizer.Unknown], Sut.Categorize(Id(77)));

    [Fact]
    public void Broken_parent_chain_returns_other()
        => Assert.Equal([ItemCategorizer.Other], Sut.Categorize(Id(4)));

    [Fact]
    public void Self_referencing_parent_does_not_loop_forever()
        => Assert.Equal([ItemCategorizer.Other], Sut.Categorize(Id(5)));

    [Fact]
    public void Invalid_mongo_id_string_returns_unknown()
        => Assert.Equal([ItemCategorizer.Unknown], Sut.Categorize("not-a-mongo-id"));
}
