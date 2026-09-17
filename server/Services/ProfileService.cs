using QuestCodex.Progress.Models;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.Profile;

namespace QuestCodex.Services;

/// <summary>SaveServer 의 메모리 프로필을 읽는다. 파일은 읽지 않는다. 캐시 없음.</summary>
[Injectable]
public class ProfileService(SaveServer saveServer, ProfileActivityService activity) : IProfileSource
{
    /// <summary>이 시간 안에 활동한 프로필을 "접속 중"으로 본다.</summary>
    public const int ActiveWindowMinutes = 30;

    public IReadOnlyList<ProfileSummary> List()
    {
        var result = new List<ProfileSummary>();
        foreach (var (id, profile) in saveServer.GetProfiles())
        {
            var pmc = profile.CharacterData?.PmcData;
            var hasCharacter = pmc?.Info is not null;
            var lastSession = pmc?.Stats?.Eft?.LastSessionDate is { } ts and > 0
                ? DateTimeOffset.FromUnixTimeSeconds(ts)
                : (DateTimeOffset?)null;

            result.Add(new ProfileSummary(
                id.ToString(),
                NicknameOf(profile, id),
                pmc?.Info?.Level,
                pmc?.Info?.Side,
                hasCharacter,
                activity.ActiveWithinLastMinutes(id, ActiveWindowMinutes),
                lastSession));
        }

        return result.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.LastSessionAt).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    public ProfileLookup Find(string id)
    {
        if (!MongoId.IsValidMongoId(id)) return new ProfileLookup(ProfileLookupState.NotFound, null, false);
        var mongoId = new MongoId(id);
        if (!saveServer.GetProfiles().TryGetValue(mongoId, out var profile))
        {
            return new ProfileLookup(ProfileLookupState.NotFound, null, false);
        }

        var pmc = profile.CharacterData?.PmcData;
        if (pmc?.Info is null) return new ProfileLookup(ProfileLookupState.NoCharacter, null, false);

        return new ProfileLookup(ProfileLookupState.Ready, pmc, activity.ActiveWithinLastMinutes(mongoId, ActiveWindowMinutes));
    }

    private static string NicknameOf(SptProfile profile, MongoId id)
        => profile.CharacterData?.PmcData?.Info?.Nickname
           ?? profile.ProfileInfo?.Username
           ?? id.ToString();
}
