using QuestCodex.Catalog;
using QuestCodex.Catalog.Locations;
using QuestCodex.Catalog.Models;

namespace QuestCodex.Tests.Catalog;

/// <summary>탈출구(10 스펙 §2.2): 서버 DB 목록 + 스냅샷 좌표·영문 이름.</summary>
public class ExitBuilderTests
{
    private const string Roubles = "5449016a4bdc2d6f028b456f";

    private static readonly Dictionary<string, string> Kr = new()
    {
        ["EXFIL_Item"] = "{0} 필요",
        ["EXFIL_INTERCHANGE_HOLE_TIP"] = "구멍에 들어가려면 배낭을 버려야 합니다.",
        [$"{Roubles} ShortName"] = "루블",
    };

    private static MapPoint P(double x) => new(x, 0, 0);

    private static SnapshotExits Positions(params string[] keys)
        => new(keys.Select((k, i) => new SnapshotExit(k, $"{k} (en)", P(i))).ToList(), [new SnapshotTransit("6", P(100))]);

    private static LocationExitRow Row(string name, string side, string req = "None", int count = 0, string? id = null, string? tip = null, double? chance = 100)
        => new(name, side, req, count, id, tip, chance);

    private static (SortedDictionary<string, List<MapExit>> Exits, List<CatalogWarning> Warnings) Build(
        IReadOnlyList<LocationExitRow> rows, SnapshotExits positions, IReadOnlyList<LocationTransitRow>? transits = null)
    {
        var warnings = new List<CatalogWarning>();
        var exits = ExitBuilder.Build(
            new Dictionary<string, SnapshotExits> { ["interchange"] = positions },
            new Dictionary<string, LocationExits> { ["interchange"] = new(rows, transits ?? []) },
            new LocaleResolver(Kr, new Dictionary<string, string>()), tpl => tpl, warnings);
        return (exits, warnings);
    }

    [Fact]
    public void Kind_comes_from_the_sides_that_list_the_exit()
    {
        var (exits, _) = Build(
            [Row("SE Exfil", "Pmc"), Row("SE Exfil", "Scav"), Row("PP Exfil", "Pmc"), Row("Scav Camp", "Scav"), Row("Interchange Cooperation", "Coop")],
            Positions("SE Exfil", "PP Exfil", "Scav Camp", "Interchange Cooperation"));

        Assert.Equal(
            [("SE Exfil", "shared"), ("PP Exfil", "pmc"), ("Scav Camp", "scav"), ("Interchange Cooperation", "shared")],
            exits["interchange"].Select(e => (e.Key, e.Kind)));
        Assert.Equal("SE Exfil (en)", exits["interchange"][0].Name);
    }

    [Fact]
    public void Same_named_pmc_and_scav_keys_at_one_spot_merge_into_shared()
    {
        var positions = new SnapshotExits(
            [
                new SnapshotExit("Road to Customs", "Road to Customs", new MapPoint(-859.05, 0, 0.49)),
                new SnapshotExit("Scav Road to Customs", "Road to Customs", new MapPoint(-859.05, 0, 0.49)),
                new SnapshotExit("Dorms V-Ex", "Dorms V-Ex", new MapPoint(10, 0, 0)),
                new SnapshotExit("Old Road Gate", "Old Road Gate", new MapPoint(11, 0, 0)),
            ],
            []);
        var (exits, _) = Build(
            [Row("Road to Customs", "Pmc"), Row("Dorms V-Ex", "Pmc"), Row("Scav Road to Customs", "Scav"), Row("Old Road Gate", "Scav")],
            positions);

        Assert.Equal(
            [("Road to Customs", "shared"), ("Dorms V-Ex", "pmc"), ("Old Road Gate", "scav")], // 이름이 다르면 같은 자리라도 따로
            exits["interchange"].Select(e => (e.Key, e.Kind)));
    }

    [Fact]
    public void Requirement_text_fills_the_amount_and_currency()
    {
        var (exits, _) = Build(
            [Row("PP Exfil", "Pmc", "TransferItem", 5000, Roubles, "EXFIL_Item", 50), Row("Hole Exfill", "Pmc", "Empty", tip: "EXFIL_INTERCHANGE_HOLE_TIP")],
            Positions("PP Exfil", "Hole Exfill"));

        var pp = exits["interchange"][0];
        Assert.Equal("5,000 루블 필요", pp.Requirement);
        Assert.Null(pp.RequirementKind);
        Assert.Equal(50, pp.Chance);
        Assert.Equal("구멍에 들어가려면 배낭을 버려야 합니다.", exits["interchange"][1].Requirement);
        Assert.Null(exits["interchange"][1].Chance); // 100% 는 싣지 않는다
    }

    [Fact]
    public void Requirements_without_locale_text_become_codes()
    {
        var (exits, _) = Build(
            [
                Row("Coop", "Coop", "ScavCooperation", tip: "EXFIL_Cooperate"), Row("Train", "Coop", "Train", 300, "0", "TIP IS HARDCODED"),
                Row("Alpinist", "Pmc", "Reference", id: "Alpinist"), Row("Elevator", "Pmc", "WorldEvent"), Row("Gate", "Pmc"),
                Row("woods_secret_minefield", "Pmc", "Secret"), Row("woods_secret_minefield", "Scav", "Secret"),
            ],
            Positions("Coop", "Train", "Alpinist", "Elevator", "Gate", "woods_secret_minefield"));

        Assert.Equal(
            [("coop", null), ("train", null), ("alpinist", null), ("switch", null), (null, null), ("secret", null)],
            exits["interchange"].Select(e => (e.RequirementKind, e.Requirement)));
    }

    [Fact]
    public void Pmc_row_wins_when_sides_differ()
    {
        var (exits, _) = Build(
            [Row("Gate", "Scav"), Row("Gate", "Pmc", "TransferItem", 5000, Roubles, "EXFIL_Item")],
            Positions("Gate"));

        Assert.Equal("5,000 루블 필요", Assert.Single(exits["interchange"]).Requirement);
    }

    [Fact]
    public void Exit_without_a_position_is_dropped_with_a_warning()
    {
        var (exits, warnings) = Build([Row(" V-Ex_light", "Pmc"), Row("Gate", "Pmc")], Positions("Gate", "Live only"));

        Assert.Equal(["Gate"], exits["interchange"].Select(e => e.Key)); // 스냅샷에만 있는 라이브 탈출구도 그리지 않는다
        var w = Assert.Single(warnings);
        Assert.Equal(WarningCodes.ExitPositionMissing, w.Code);
        Assert.Equal("interchange/ V-Ex_light", w.Detail);
    }

    [Fact]
    public void Active_transits_get_their_target_map()
    {
        var (exits, warnings) = Build(
            [], Positions(),
            [new LocationTransitRow("6", true, "bigmap"), new LocationTransitRow("7", true, "TarkovStreets"), new LocationTransitRow("99", false, "Interchange")]);

        var transit = Assert.Single(exits["interchange"]);
        Assert.Equal(new MapExit("6", "", "transit", P(100), null, null, null, "bigmap"), transit);
        Assert.Equal("interchange/transit 7", Assert.Single(warnings).Detail); // 활성인데 좌표가 없는 것만 경고
    }

    [Fact]
    public void Transits_on_a_map_without_any_snapshot_transit_are_skipped_silently()
    {
        var warnings = new List<CatalogWarning>();
        var exits = ExitBuilder.Build(
            new Dictionary<string, SnapshotExits> { ["labyrinth"] = new([], []) },
            new Dictionary<string, LocationExits> { ["labyrinth"] = new([], [new LocationTransitRow("15", true, "Sandbox")]) },
            new LocaleResolver(Kr, new Dictionary<string, string>()), tpl => tpl, warnings);

        Assert.Empty(exits);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Map_without_snapshot_data_is_skipped_silently()
    {
        var warnings = new List<CatalogWarning>();
        var exits = ExitBuilder.Build(
            new Dictionary<string, SnapshotExits>(),
            new Dictionary<string, LocationExits> { ["icebreaker"] = new([Row("Gate", "Pmc")], []) },
            new LocaleResolver(Kr, new Dictionary<string, string>()), tpl => tpl, warnings);

        Assert.Empty(exits);
        Assert.Empty(warnings);
    }
}
