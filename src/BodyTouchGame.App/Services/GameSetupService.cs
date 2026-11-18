using System.Text.Json;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Persistence;
using BodyTouchGame.Core.Players;
using BodyTouchGame.Core.Serialization;
using BodyTouchGame.Core.Targets;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.App.Services;

/// <summary>
/// The game being prepared (players → configuration → zones → game). Shared by the setup screens and the game screen.
/// The last configuration is remembered in preferences and offered next time.
/// </summary>
public sealed class GameSetupService(IPreferences preferences, IExclusionRepository exclusions, ILevelOverrideRepository overrides,
    ICustomPointRepository customPoints)
{
    private const string LastConfigKey = "setup.lastConfig";
    private const string LastPresetKey = "setup.lastPreset";
    private const string RoleOrderKey = "setup.roleOrder";

    private GameConfig? _config;

    public List<Player> Players { get; } = [];

    public RoleOrder RoleOrder
    {
        get => Enum.TryParse<RoleOrder>(preferences.Get(RoleOrderKey, nameof(RoleOrder.AllPairs)), out var order) ? order : RoleOrder.AllPairs;
        set => preferences.Set(RoleOrderKey, value.ToString());
    }

    /// <summary>The preset the configuration came from (null after manual changes that match no preset).</summary>
    public long? PresetId
    {
        get => preferences.Get(LastPresetKey, -1L) is var id and >= 0 ? id : null;
        set => preferences.Set(LastPresetKey, value ?? -1L);
    }

    /// <summary>Display name of the preset for headers ("Runda 3 z 9 · Średni").</summary>
    public string PresetLabel { get; set; } = string.Empty;

    public GameConfig Config
    {
        get
        {
            if (_config is not null) return _config;
            var json = preferences.Get(LastConfigKey, string.Empty);
            try
            {
                _config = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize(json, CoreJsonContext.Default.GameConfig);
            }
            catch (JsonException)
            {
                _config = null;
            }

            return _config = _config?.Normalized() ?? BuiltInPresets.Medium;
        }
        set
        {
            _config = value;
            preferences.Set(LastConfigKey, JsonSerializer.Serialize(value, CoreJsonContext.Default.GameConfig));
        }
    }

    /// <summary>Union of the excluded zones of the selected players.</summary>
    public async Task<HashSet<string>> ExcludedZonesOfPlayersAsync()
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in Players) all.UnionWith((await exclusions.GetAsync(player.Id)).ZoneIds);
        return all;
    }

    /// <summary>Drawable targets per model (each model's own exclusions), for the game session.</summary>
    public async Task<Dictionary<long, IReadOnlyList<Target>>> BuildTargetPoolsAsync(ZoneCatalog catalog)
    {
        var builder = new TargetPoolBuilder(catalog);
        var levelOverrides = await overrides.GetAllAsync();
        var points = await customPoints.GetAllAsync();
        var pools = new Dictionary<long, IReadOnlyList<Target>>();
        foreach (var player in Players)
        {
            var excluded = (await exclusions.GetAsync(player.Id)).ZoneIds.ToList();
            pools[player.Id] = builder.Build(Config.ZoneSet, excluded, levelOverrides, points);
        }

        return pools;
    }

    /// <summary>Zone set preview: zones and custom points still drawable with the selected players' exclusions, and the
    /// ones of the set that those exclusions remove.</summary>
    public async Task<ZoneSetPreview> PreviewAsync(ZoneCatalog catalog, ZoneSetSelection selection)
    {
        var builder = new TargetPoolBuilder(catalog);
        var levelOverrides = await overrides.GetAllAsync();
        var points = await customPoints.GetAllAsync();
        var all = builder.Build(selection, [], levelOverrides, points);
        var remaining = builder.Build(selection, await ExcludedZonesOfPlayersAsync(), levelOverrides, points).ToHashSet();
        return new ZoneSetPreview(
            all.Where(remaining.Contains).OfType<ZoneTarget>().Select(t => t.ZoneId).ToList(),
            all.Where(t => !remaining.Contains(t)).OfType<ZoneTarget>().Select(t => t.ZoneId).ToList(),
            all.Where(remaining.Contains).OfType<CustomPointTarget>().Select(t => t.PointId).ToHashSet(),
            all.Count(remaining.Contains),
            all.Count - remaining.Count,
            points);
    }
}

/// <summary>What a zone set draws for the selected players (counts include custom points).</summary>
public sealed record ZoneSetPreview(
    IReadOnlyList<string> DrawableZones,
    IReadOnlyList<string> ExcludedZones,
    IReadOnlySet<long> DrawablePoints,
    int DrawableCount,
    int ExcludedCount,
    IReadOnlyList<CustomPoint> AllPoints);
