using QuestCodex.Catalog.Models;
using QuestCodex.Services;

namespace QuestCodex.Tests.Services;

public class QuestZoneSnapshotTests
{
    [Fact]
    public void Parse_reads_points_per_map_and_zone()
    {
        const string json = """
            { "collectedWith": "EFT 0.16.9.40743",
              "zones": {
                "bigmap": { "fuel4": [{ "x": -334.93, "y": 2.22, "z": -163.46 }], "two": [{ "x": 1, "y": 2, "z": 3 }, { "x": 4, "y": 5, "z": 6 }] },
                "rezervbase": { "fuel4": [{ "x": -334.93, "y": -101.46, "z": -163.46 }] } } }
            """;

        var (collectedWith, zones, doors) = QuestZoneSnapshot.Parse(json);

        Assert.Equal("EFT 0.16.9.40743", collectedWith);
        Assert.Empty(doors); // doors 가 없는 구버전 스냅샷도 읽힌다
        Assert.Equal([new MapPoint(-334.93, 2.22, -163.46)], zones["bigmap"]["fuel4"]);
        Assert.Equal(2, zones["bigmap"]["two"].Count);
        Assert.Equal(-101.46, zones["rezervbase"]["fuel4"][0].Y);
    }

    [Fact]
    public void Parse_rejects_missing_zones()
        => Assert.ThrowsAny<Exception>(() => QuestZoneSnapshot.Parse("""{ "collectedWith": "x" }"""));

    [Fact]
    public void Bundled_snapshot_file_exists_next_to_assembly_and_parses()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "quest-zones.json");
        Assert.True(File.Exists(path), $"missing {path}");

        var (_, zones, doors) = QuestZoneSnapshot.Parse(File.ReadAllText(path));

        Assert.True(zones.Count >= 12, $"only {zones.Count} maps");
        Assert.All(zones.Keys, map => Assert.Equal(map.ToLowerInvariant(), map));
        foreach (var fuel in new[] { "fuel1", "fuel2", "fuel3", "fuel4" }) Assert.True(zones["bigmap"].ContainsKey(fuel), fuel);
        Assert.Equal(34, doors["bigmap"].Count); // 10/1 재덤프에서 Military checkpoint 문 하나 추가
        Assert.All(doors.Values.SelectMany(d => d), d => Assert.Contains(d.Type, new[] { "Door", "KeycardDoor" }));
    }

    [Fact]
    public void Parse_reads_bounds_size_as_areas()
    {
        const string json = """
            { "zones": { "woods": {
                "kill_zone": [{ "x": 410, "y": 13.6, "z": -329, "sx": 500, "sz": 1600, "y0": -36.4, "y1": 63.6 }],
                "offset": [{ "x": 1, "y": 2, "z": 3, "sx": 10, "sz": 20, "cx": 5, "cz": 6 }],
                "bare": [{ "x": 7, "y": 8, "z": 9 }] } } }
            """;

        var snapshot = QuestZoneSnapshot.Parse(json);

        Assert.Equal([new MapArea(new MapPoint(410, 13.6, -329), 500, 1600, 0, -36.4, 63.6)], snapshot.Areas["woods"]["kill_zone"]);
        // 영역 중심은 Bounds 중심(cx, cz), 높이는 존 위치 높이(층 판정용). 점은 여전히 Position
        // y0·y1 이 없는 구버전 스냅샷이면 높이 범위는 위치 높이 한 점
        Assert.Equal([new MapArea(new MapPoint(5, 2, 6), 10, 20, 0, 2, 2)], snapshot.Areas["woods"]["offset"]);
        Assert.Equal([new MapPoint(1, 2, 3)], snapshot.Zones["woods"]["offset"]);
        Assert.False(snapshot.Areas["woods"].ContainsKey("bare"));
    }

    /// <summary>
    /// 덤프 0.0.2+ 형식: 점마다 콜라이더 상자 목록(실제 중심·크기·회전·높이 범위). 헌병대 - 쇼핑몰 경비대 존은 외곽 사각형
    /// 238×139 가 아니라 224.6×104.7 을 9.15° 돌린 상자였다.
    /// </summary>
    [Fact]
    public void Parse_reads_collider_boxes_with_rotation()
    {
        const string json = """
            { "zones": { "tarkovstreets": {
                "quest_zone_kill_stilo": [{ "x": -102.9, "y": 8.6, "z": -25.6,
                  "boxes": [{ "cx": -102.9, "cz": -25.6, "sx": 224.6, "sz": 104.7, "r": 9.15, "y0": -6.4, "y1": 23.6 }] }],
                "two_boxes": [{ "x": 1, "y": 2, "z": 3,
                  "boxes": [{ "cx": 1, "cz": 3, "sx": 3, "sz": 4, "r": 30, "y0": 0, "y1": 4 },
                            { "cx": 5, "cz": 6, "sx": 2, "sz": 2, "r": 30, "y0": 0, "y1": 4 }] }] } } }
            """;

        var snapshot = QuestZoneSnapshot.Parse(json);

        Assert.Equal(
            [new MapArea(new MapPoint(-102.9, 8.6, -25.6), 224.6, 104.7, 9.15, -6.4, 23.6)],
            snapshot.Areas["tarkovstreets"]["quest_zone_kill_stilo"]);
        Assert.Equal(2, snapshot.Areas["tarkovstreets"]["two_boxes"].Count);
        Assert.Equal([new MapPoint(1, 2, 3)], snapshot.Zones["tarkovstreets"]["two_boxes"]);
    }

    private const string VariantJson = """
        { "collectedWith": "x",
          "zones": {
            "interchange": { "moved": [{ "x": 274, "y": 22, "z": 14, "sx": 2, "sz": 2 }], "gone": [{ "x": 1, "y": 1, "z": 1 }] },
            "woods": { "w": [{ "x": 5, "y": 5, "z": 5 }] } },
          "doors": { "interchange": [{ "key": "k1", "type": "Door", "x": 1, "y": 2, "z": 3 }] },
          "variants": { "manimal": {
            "zones": { "interchange": { "moved": [{ "x": 429, "y": 28, "z": 125 }], "new_zone": [{ "x": 530, "y": 32, "z": 82 }] } },
            "doors": { "interchange": [{ "key": "k2", "type": "Door", "x": -379, "y": 2, "z": -207 }] } } } }
        """;

    [Fact]
    public void Variants_are_parsed_but_not_applied_without_an_active_mod()
    {
        var snapshot = QuestZoneSnapshot.Parse(VariantJson);

        Assert.Single(snapshot.Variants);
        Assert.Same(snapshot, snapshot.WithVariants(new Dictionary<string, string>()));
        Assert.Equal(274, snapshot.Zones["interchange"]["moved"][0].X);
        Assert.Equal("k1", snapshot.Doors["interchange"][0].KeyTpl);
    }

    /// <summary>
    /// 확장 인터체인지: 맵 하나를 통째로 바꾼다. 옮겨진 존은 새 좌표, 변형에 없는 존은 바닐라로 돌아가지 않고 빠지고,
    /// 변형 존에 크기가 없으면 바닐라 영역도 남지 않는다. 다른 맵은 그대로.
    /// </summary>
    [Fact]
    public void Active_variant_replaces_the_whole_map()
    {
        var snapshot = QuestZoneSnapshot.Parse(VariantJson).WithVariants(new Dictionary<string, string> { ["interchange"] = "manimal" });

        var interchange = snapshot.Zones["interchange"];
        Assert.Equal([new MapPoint(429, 28, 125)], interchange["moved"]);
        Assert.True(interchange.ContainsKey("new_zone"));
        Assert.False(interchange.ContainsKey("gone"));
        Assert.False(snapshot.Areas.ContainsKey("interchange"));
        Assert.Equal(["k2"], snapshot.Doors["interchange"].Select(d => d.KeyTpl));
        Assert.Equal(5, snapshot.Zones["woods"]["w"][0].X);
    }

    [Fact]
    public void Unknown_variant_or_map_keeps_vanilla()
    {
        var snapshot = QuestZoneSnapshot.Parse(VariantJson);

        Assert.Same(snapshot, snapshot.WithVariants(new Dictionary<string, string> { ["interchange"] = "other" }));
        Assert.Same(snapshot, snapshot.WithVariants(new Dictionary<string, string> { ["woods"] = "manimal" }));
    }

    [Fact]
    public void Bundled_snapshot_carries_the_expanded_interchange_variant()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Data", "quest-zones.json");
        var snapshot = QuestZoneSnapshot.Parse(File.ReadAllText(path));

        var vanilla = snapshot.Zones["interchange"]["place_WARBLOOD_04_2"][0];
        var expanded = snapshot.WithVariants(new Dictionary<string, string> { ["interchange"] = "manimal" });
        var moved = expanded.Zones["interchange"]["place_WARBLOOD_04_2"][0];

        // 2026-10-03 확장 덤프: 바닐라 퀘스트 존이 동쪽 확장 구역으로 190m 옮겨졌다
        Assert.True(Math.Abs(moved.X - vanilla.X) > 100, $"{vanilla} -> {moved}");
        Assert.True(expanded.Zones["interchange"].ContainsKey("shorl_exit_sniper_opt"));
        Assert.False(snapshot.Zones["interchange"].ContainsKey("shorl_exit_sniper_opt"));
    }

    [Fact]
    public void Parse_reads_locked_doors_per_map()
    {
        const string json = """
            { "zones": {},
              "doors": { "laboratory": [
                { "key": "5c1d0efb86f7744baf2e7b7b", "type": "KeycardDoor", "x": -120.5, "y": 0.1, "z": -330.2 },
                { "key": "5c1e2a1e86f77431ea0ea84c", "type": "Door", "x": 1, "y": 2, "z": 3 } ] } }
            """;

        var (_, _, doors) = QuestZoneSnapshot.Parse(json);

        var labs = doors["laboratory"];
        Assert.Equal(2, labs.Count);
        Assert.Equal(new SnapshotDoor("5c1d0efb86f7744baf2e7b7b", "KeycardDoor", new MapPoint(-120.5, 0.1, -330.2)), labs[0]);
    }
}
