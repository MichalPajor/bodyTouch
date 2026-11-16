using System.Text.Json;
using BodyTouchGame.Core.Config;
using BodyTouchGame.Core.Serialization;
using SQLite;

namespace BodyTouchGame.Data;

/// <summary>
/// The app database (SQLite file). Creates and migrates the schema on first use and seeds the built-in presets.
/// One instance per database file; repositories share it.
/// </summary>
public sealed class BodyTouchDatabase : IAsyncDisposable
{
    /// <summary>Current schema version. Add a step to <see cref="Migrations"/> when the schema changes.</summary>
    public const int SchemaVersion = 1;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public BodyTouchDatabase(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Path = path;
        Connection = new SQLiteAsyncConnection(path,
            SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex,
            storeDateTimeAsTicks: true);
    }

    public string Path { get; }

    internal SQLiteAsyncConnection Connection { get; }

    /// <summary>Schema migrations: index i upgrades version i to i + 1.</summary>
    private static readonly Func<SQLiteAsyncConnection, Task>[] Migrations =
    [
        // 0 -> 1: initial schema (spec 11).
        async db =>
        {
            await db.CreateTableAsync<PlayerEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<ExclusionProfileEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<GamePresetEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<CustomPointEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<LevelOverrideEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<SessionEntity>().ConfigureAwait(false);
            await db.CreateTableAsync<RoundEntity>().ConfigureAwait(false);
        },
    ];

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_initialized) return;
            await Connection.CreateTableAsync<SchemaInfoEntity>().ConfigureAwait(false);
            var info = await Connection.FindAsync<SchemaInfoEntity>(1).ConfigureAwait(false) ?? new SchemaInfoEntity { Version = 0 };
            if (info.Version > SchemaVersion)
                throw new InvalidOperationException($"The database was created by a newer app version (schema {info.Version}).");

            for (var version = info.Version; version < SchemaVersion; version++)
            {
                await Migrations[version](Connection).ConfigureAwait(false);
                info.Version = version + 1;
                await Connection.InsertOrReplaceAsync(info).ConfigureAwait(false);
            }

            await SeedBuiltInPresetsAsync().ConfigureAwait(false);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<int> GetSchemaVersionAsync()
    {
        await InitializeAsync().ConfigureAwait(false);
        return (await Connection.FindAsync<SchemaInfoEntity>(1).ConfigureAwait(false))?.Version ?? 0;
    }

    /// <summary>Inserts missing built-in presets and updates their configuration to the current values.</summary>
    private async Task SeedBuiltInPresetsAsync()
    {
        var existing = await Connection.Table<GamePresetEntity>().Where(p => p.IsBuiltIn).ToListAsync().ConfigureAwait(false);
        foreach (var (key, config) in BuiltInPresets.All)
        {
            var json = JsonSerializer.Serialize(config, CoreJsonContext.Default.GameConfig);
            var row = existing.FirstOrDefault(p => p.Name == key);
            if (row is null)
                await Connection.InsertAsync(new GamePresetEntity { Name = key, IsBuiltIn = true, ConfigJson = json }).ConfigureAwait(false);
            else if (row.ConfigJson != json)
            {
                row.ConfigJson = json;
                await Connection.UpdateAsync(row).ConfigureAwait(false);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Connection.CloseAsync().ConfigureAwait(false);
        _initLock.Dispose();
    }
}
