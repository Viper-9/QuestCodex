using QuestCodex.Progress.Models;
using SPTarkov.Server.Core.Models.Eft.Common;

namespace QuestCodex.Services;

public enum ProfileLookupState { NotFound, NoCharacter, Ready }

public sealed record ProfileLookup(ProfileLookupState State, PmcData? Pmc, bool IsActive);

/// <summary>컨트롤러가 의존하는 프로필 공급자. 테스트에서 가짜로 바꾼다.</summary>
public interface IProfileSource
{
    IReadOnlyList<ProfileSummary> List();
    ProfileLookup Find(string id);
}
