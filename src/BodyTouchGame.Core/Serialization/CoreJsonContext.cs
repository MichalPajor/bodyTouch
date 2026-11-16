using System.Text.Json.Serialization;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Zones;

namespace BodyTouchGame.Core.Serialization;

/// <summary>body_zones.json as stored on disk (docs/formaty/body_zones.md).</summary>
public sealed record BodyZonesFileDto(int Version, int TriangleCount, List<ZoneDto> Zones, Dictionary<string, int[]> TriangleZones);

public sealed record ZoneDto(string Id, ZoneLevel Level, string? ParentId, string NamePl, ZoneSide Side, bool Drawable = true);

/// <summary>Source-generated JSON for Core types (no reflection; safe with trimming). Enums as strings.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(GameConfig))]
[JsonSerializable(typeof(ZoneSetSelection))]
[JsonSerializable(typeof(BodyZonesFileDto))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<ZoneLevel>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<long>))]
public sealed partial class CoreJsonContext : JsonSerializerContext;
