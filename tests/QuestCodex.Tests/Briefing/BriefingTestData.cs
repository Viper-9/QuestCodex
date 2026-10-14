using QuestCodex.Catalog.Models;
using QuestCodex.Progress.Models;
using CatalogModel = QuestCodex.Catalog.Models.Catalog;

namespace QuestCodex.Tests.Briefing;

// 웹 derive.test.ts 의 헬퍼(prep, item, obj, quest, catalog, qp, progress)를 그대로 옮겼다. 웹 테스트 사례를 같은 모양으로 가져오기 위해서.
internal static class B
{
    public static readonly ItemRef Marker = new("ms2000", "MS2000 Marker");
    public static readonly ItemRef Jammer = new("jam", "Signal Jammer");
    public static readonly ItemRef TagA = new("tagA", "BEAR dogtag");
    public static readonly ItemRef TagB = new("tagB", "BEAR dogtag");

    public static ObjectivePrep Prep(
        PrepItem? item = null, IReadOnlyList<string>? mapKeys = null, IReadOnlyList<ItemRef>? weapons = null,
        IReadOnlyList<string>? calibers = null, IReadOnlyList<IReadOnlyList<ItemRef>>? weaponMods = null,
        IReadOnlyList<IReadOnlyList<IReadOnlyList<ItemRef>>>? equipment = null, IReadOnlyList<ItemRef>? forbidden = null,
        bool oneRaid = false, IReadOnlyList<string>? exitStatuses = null, string? exitName = null)
        => new([], mapKeys ?? [], item, weapons ?? [], calibers ?? [], weaponMods ?? [], equipment ?? [], forbidden ?? [],
            oneRaid, exitStatuses ?? [], exitName);

    public static PrepItem Item(string action = "handover", IReadOnlyList<ItemRef>? items = null, double count = 1, bool fir = false)
        => new(action, items ?? [Marker], count, fir, null, null, null, null);

    public static Objective Obj(string id, string type, ObjectivePrep? prep = null, double? targetCount = null,
        string? text = null, IReadOnlyList<ObjectiveLocation>? locations = null)
        => new(id, type, text ?? id, targetCount, null, prep) { Locations = locations ?? Array.Empty<ObjectiveLocation>() };

    public static IReadOnlyList<ObjectiveLocation> At(string map, double x = 0, double y = 0, double z = 0)
        => [new ObjectiveLocation(map, [new MapPoint(x, y, z)])];

    public static CatalogQuest Quest(string id, IReadOnlyList<Objective> objectives, string? locationKey = null,
        string traderId = "t1", string? name = null) => new()
    {
        Id = id, Name = name ?? id, Description = "", TraderId = traderId, Side = "Pmc", IsVanilla = true,
        LocationKey = locationKey, Requirements = [], Prerequisites = [], Objectives = objectives,
        Rewards = new QuestRewards([], [], []),
    };

    public static CatalogModel Catalog(
        IEnumerable<CatalogQuest> quests, string lang = "en",
        SortedDictionary<string, List<MapExit>>? exits = null,
        SortedDictionary<string, List<LockedDoor>>? doors = null,
        SortedDictionary<string, string>? variants = null,
        SortedDictionary<string, CatalogTrader>? traders = null)
        => new("", "", DateTimeOffset.UnixEpoch, lang, traders ?? new(StringComparer.Ordinal),
            new(quests.ToDictionary(q => q.Id), StringComparer.Ordinal), new(), [], [], new(),
            doors ?? new(StringComparer.Ordinal), variants ?? new(StringComparer.Ordinal), exits ?? new(StringComparer.Ordinal), new());

    public static ObjectiveProgress Op(double current, double? target, bool done = false) => new(current, target, done);

    public static QuestProgress Qp(string status, Dictionary<string, ObjectiveProgress>? objectives = null)
        => new(status, null, null, [], new(objectives ?? new(), StringComparer.Ordinal));

    public static ProfileProgress Progress(Dictionary<string, QuestProgress> quests, Dictionary<string, ItemHolding>? inventory = null)
        => new("p", "p", 1, "Usec", true, new(quests, StringComparer.Ordinal), new(),
            new(inventory ?? new(), StringComparer.Ordinal), []);

    public static Dictionary<string, ItemHolding> Inv(params (string Tpl, int Count, int Fir)[] xs)
        => xs.ToDictionary(x => x.Tpl, x => new ItemHolding(x.Count, x.Fir));
}

// 저장소 루트(QuestCodex.slnx 가 있는 폴더)를 테스트 bin 폴더에서 위로 올라가며 찾는다.
internal static class TestPaths
{
    public static readonly string? RepoRoot = Find();

    public static string MapIndexJson => Path.Combine(RepoRoot!, "web", "public", "maps", "index.json");

    private static string? Find()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "QuestCodex.slnx"))) return dir.FullName;
        }
        return null;
    }
}
