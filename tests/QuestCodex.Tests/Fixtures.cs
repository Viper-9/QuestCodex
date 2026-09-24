using Microsoft.Extensions.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Utils.Json;
using Spectre.Console;
using PmcInfo = SPTarkov.Server.Core.Models.Eft.Common.Tables.Info;
using Color = Spectre.Console.Color;

namespace QuestCodex.Tests;

/// <summary>SPT 모델을 최소 필드로 생성하는 헬퍼. 모든 테스트가 공유한다.</summary>
public static class Fixtures
{
    /// <summary>n 을 24자 hex MongoId 로. Id(1) == "000000000000000000000001".</summary>
    public static MongoId Id(int n) => new(n.ToString("x24"));

    public static readonly MongoId Prapor = new("54cb50c76803fa8b248b4571");
    public static readonly MongoId Therapist = new("54cb57776803fa99248b456e");

    public static Quest Quest(
        MongoId id,
        MongoId? trader = null,
        List<QuestCondition>? start = null,
        List<QuestCondition>? finish = null,
        Dictionary<string, List<Reward>>? rewards = null,
        string image = "") => new()
    {
        Id = id,
        TraderId = trader ?? Prapor,
        Side = "Pmc",
        Image = image,
        Name = $"{id} name",
        Description = $"{id} description",
        Location = "any",
        Type = QuestTypeEnum.Completion,
        Restartable = false,
        CanShowNotificationsInGame = true,
        Conditions = new QuestConditionTypes { AvailableForStart = start ?? [], AvailableForFinish = finish ?? [] },
        Rewards = rewards ?? new() { ["Started"] = [], ["Success"] = [], ["Fail"] = [] },
    };

    public static QuestCondition QuestCond(MongoId condId, MongoId target, params QuestStatusEnum[] statuses) => new()
    {
        Id = condId,
        ConditionType = "Quest",
        DynamicLocale = false,
        Target = new ListOrT<string>(null, target),
        Status = statuses.Length == 0 ? [QuestStatusEnum.Success] : [.. statuses],
        AvailableAfter = 0,
    };

    public static QuestCondition LevelCond(MongoId condId, double value, string compare = ">=") => new()
    {
        Id = condId, ConditionType = "Level", DynamicLocale = false, Value = value, CompareMethod = compare,
    };

    public static QuestCondition TraderCond(MongoId condId, string type, MongoId trader, double value, string compare = ">=") => new()
    {
        Id = condId, ConditionType = type, DynamicLocale = false,
        Target = new ListOrT<string>(null, trader), Value = value, CompareMethod = compare,
    };

    public static QuestCondition FinishCond(MongoId condId, string type = "CounterCreator", double? value = null, MongoId? target = null) => new()
    {
        Id = condId, ConditionType = type, DynamicLocale = false, Value = value,
        Target = target is null ? null : new ListOrT<string>([target.Value.ToString()], null),
    };

    public static Item Item(MongoId tpl, MongoId? id = null) => new() { Id = id ?? Id(9000), Template = tpl };

    public static Reward Reward(RewardType type, double? value = null, List<Item>? items = null,
        string? target = null, int? loyalty = null, StringOrInt? traderId = null) => new()
    {
        Id = Id(8000), Type = type, Value = value, Items = items, Target = target, LoyaltyLevel = loyalty, TraderId = traderId,
    };

    public static TemplateItem Template(MongoId id, MongoId parent, string? name = null)
    {
        var t = new TemplateItem { Id = id, Parent = parent };
        if (name is not null) t.Name = name;
        return t;
    }

    public static TraderBase Trader(MongoId id, string nickname, string avatar = "") => new()
    {
        Id = id, Nickname = nickname, Avatar = avatar, Name = nickname,
    };

    public static PmcData Pmc(int level = 1, string side = "Usec", string nickname = "tester") => new()
    {
        Info = new PmcInfo { Level = level, Side = side, Nickname = nickname },
        Quests = [],
        TradersInfo = new(),
        TaskConditionCounters = new(),
    };

    public static QuestStatus ProfileQuest(MongoId qid, QuestStatusEnum status, double start = 0) => new()
    {
        QId = qid, Status = status, StartTime = start, StatusTimers = new(),
    };

    public static IReadOnlyDictionary<MongoId, T> Dict<T>(params (MongoId id, T value)[] items)
        => items.ToDictionary(x => x.id, x => x.value);
}

/// <summary>ISptLogger 스텁. 메시지를 레벨별로 모아둔다.</summary>
public sealed class RecordingLogger<T> : ISptLogger<T>
{
    public List<(string Level, string Message)> Entries { get; } = [];

    public void LogWithColor(string data, Color? textColor = null, Color? backgroundColor = null, Exception? ex = null) => Entries.Add(("Info", data));
    public void Success(string data, Exception? ex = null) => Entries.Add(("Success", data));
    public void Error(string data, Exception? ex = null) => Entries.Add(("Error", data));
    public void Warning(string data, Exception? ex = null) => Entries.Add(("Warning", data));
    public void Info(string data, Exception? ex = null) => Entries.Add(("Info", data));
    public void Debug(string data, Exception? ex = null) => Entries.Add(("Debug", data));
    public void Critical(string data, Exception? ex = null) => Entries.Add(("Critical", data));
    public void Log(LogLevel level, string data, Color? textColor = null, Color? backgroundColor = null, Exception? ex = null) => Entries.Add((level.ToString(), data));
    public bool IsLogEnabled(LogLevel level) => true;
}
