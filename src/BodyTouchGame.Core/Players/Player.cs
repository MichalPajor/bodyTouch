namespace BodyTouchGame.Core.Players;

/// <summary>A player (spec 10). The name is unique (case-insensitive).</summary>
public sealed record Player(long Id, string Name, string AvatarColor, string? AvatarEmoji, DateTimeOffset CreatedAt)
{
    public const int MaxNameLength = 24;

    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) problems.Add("Name is required.");
        else if (Name.Trim().Length > MaxNameLength) problems.Add($"Name is longer than {MaxNameLength} characters.");
        if (string.IsNullOrWhiteSpace(AvatarColor)) problems.Add("Avatar colour is required.");
        return problems;
    }
}

/// <summary>Zones a player does not want to be touched on (spec 7, item 4). Ids are normalised (distinct, sorted).</summary>
public sealed record ExclusionProfile
{
    public ExclusionProfile(long playerId, IEnumerable<string>? zoneIds = null)
    {
        PlayerId = playerId;
        ZoneIds = (zoneIds ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public long PlayerId { get; }
    public IReadOnlyList<string> ZoneIds { get; }

    public bool Equals(ExclusionProfile? other) => other is not null && PlayerId == other.PlayerId && ZoneIds.SequenceEqual(other.ZoneIds);

    public override int GetHashCode() => HashCode.Combine(PlayerId, ZoneIds.Count);
}
