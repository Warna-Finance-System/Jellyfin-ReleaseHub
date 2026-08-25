using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ReleaseHub.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ReleaseHub.Services;

/// <summary>
/// ReleaseHub's persistent store: provider mappings, cached provider data, follows and sync state.
/// </summary>
/// <remarks>
/// <para>
/// Backed by a SQLite database inside the plugin's own data folder. ReleaseHub never touches Jellyfin's
/// database — it only reads the library through <c>ILibraryManager</c> — so nothing here can corrupt or
/// lock the server's own storage.
/// </para>
/// <para>
/// The <c>Microsoft.Data.Sqlite</c> provider and its native <c>e_sqlite3</c> library already ship with
/// Jellyfin, so the plugin reuses them rather than bundling a second copy.
/// </para>
/// </remarks>
public sealed class CacheService : IDisposable
{
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<CacheService> _logger;
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initGate = new(1, 1);

    private bool _initialized;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheService"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public CacheService(ILogger<CacheService> logger)
    {
        _logger = logger;

        var folder = Plugin.Instance?.DataFolderPath
            ?? Path.Combine(Path.GetTempPath(), "jellyfin-releasehub");

        Directory.CreateDirectory(folder);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(folder, "releasehub.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    /// <summary>
    /// Creates the schema if it does not exist yet.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();

            // WAL keeps reads from blocking the synchronization task's writes, which matters because
            // the calendar is read while a sync may be running.
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

                CREATE TABLE IF NOT EXISTS meta (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS provider_mapping (
                    jellyfin_id TEXT    NOT NULL,
                    provider    INTEGER NOT NULL,
                    provider_id TEXT    NOT NULL,
                    confidence  INTEGER NOT NULL,
                    reason      TEXT,
                    is_manual   INTEGER NOT NULL DEFAULT 0,
                    updated_utc TEXT    NOT NULL,
                    PRIMARY KEY (jellyfin_id, provider)
                );

                CREATE TABLE IF NOT EXISTS pending_match (
                    jellyfin_id     TEXT    NOT NULL,
                    provider        INTEGER NOT NULL,
                    title           TEXT    NOT NULL,
                    year            INTEGER,
                    candidates_json TEXT    NOT NULL,
                    updated_utc     TEXT    NOT NULL,
                    PRIMARY KEY (jellyfin_id, provider)
                );

                CREATE TABLE IF NOT EXISTS series_cache (
                    provider     INTEGER NOT NULL,
                    provider_id  TEXT    NOT NULL,
                    payload_json TEXT    NOT NULL,
                    fetched_utc  TEXT    NOT NULL,
                    PRIMARY KEY (provider, provider_id)
                );

                CREATE TABLE IF NOT EXISTS release_cache (
                    provider     INTEGER NOT NULL,
                    provider_id  TEXT    NOT NULL,
                    release_key  TEXT    NOT NULL,
                    release_utc  TEXT,
                    payload_json TEXT    NOT NULL,
                    fetched_utc  TEXT    NOT NULL,
                    PRIMARY KEY (provider, provider_id, release_key)
                );

                CREATE INDEX IF NOT EXISTS ix_release_cache_date
                    ON release_cache (release_utc);

                CREATE TABLE IF NOT EXISTS followed_item (
                    user_id     TEXT    NOT NULL,
                    provider    INTEGER NOT NULL,
                    provider_id TEXT    NOT NULL,
                    title       TEXT    NOT NULL,
                    is_anime    INTEGER NOT NULL,
                    poster_url  TEXT,
                    added_utc   TEXT    NOT NULL,
                    PRIMARY KEY (user_id, provider, provider_id)
                );
                """;

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            await SetMetaAsync(
                connection,
                "schema_version",
                SchemaVersion.ToString(CultureInfo.InvariantCulture),
                cancellationToken).ConfigureAwait(false);

            _initialized = true;
            _logger.LogDebug("ReleaseHub cache ready");
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Stores a confirmed mapping between a Jellyfin item and a provider series.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item.</param>
    /// <param name="match">The accepted match.</param>
    /// <param name="isManual">Whether a user chose this mapping explicitly.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// A manual mapping is never overwritten by an automatic one. Someone who has corrected a match by
    /// hand must not have the next synchronization undo their decision.
    /// </remarks>
    public async Task SaveMappingAsync(
        Guid jellyfinItemId,
        ProviderMatch match,
        bool isManual,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(match);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO provider_mapping
                (jellyfin_id, provider, provider_id, confidence, reason, is_manual, updated_utc)
            VALUES ($jellyfin, $provider, $providerId, $confidence, $reason, $manual, $updated)
            ON CONFLICT (jellyfin_id, provider) DO UPDATE SET
                provider_id = excluded.provider_id,
                confidence  = excluded.confidence,
                reason      = excluded.reason,
                is_manual   = excluded.is_manual,
                updated_utc = excluded.updated_utc
            WHERE provider_mapping.is_manual = 0 OR excluded.is_manual = 1;
            """;

        command.Parameters.AddWithValue("$jellyfin", jellyfinItemId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)match.Series.Provider);
        command.Parameters.AddWithValue("$providerId", match.Series.ProviderId);
        command.Parameters.AddWithValue("$confidence", (int)match.Confidence);
        command.Parameters.AddWithValue("$reason", match.Reason);
        command.Parameters.AddWithValue("$manual", isManual ? 1 : 0);
        command.Parameters.AddWithValue("$updated", Iso(DateTime.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // A confirmed mapping settles the question, so drop any outstanding request for confirmation.
        await ClearPendingMatchAsync(
            connection,
            jellyfinItemId,
            match.Series.Provider,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the stored mapping for a Jellyfin item and provider.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The stored mapping, or <see langword="null"/>.</returns>
    public async Task<StoredMapping?> GetMappingAsync(
        Guid jellyfinItemId,
        ReleaseProviderKind provider,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT provider_id, confidence, is_manual, updated_utc
            FROM provider_mapping
            WHERE jellyfin_id = $jellyfin AND provider = $provider;
            """;

        command.Parameters.AddWithValue("$jellyfin", jellyfinItemId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)provider);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new StoredMapping(
            jellyfinItemId,
            provider,
            reader.GetString(0),
            (MatchConfidence)reader.GetInt32(1),
            reader.GetInt32(2) == 1,
            ParseIso(reader.GetString(3)));
    }

    /// <summary>
    /// Records a series that could not be matched confidently, for later human confirmation.
    /// </summary>
    /// <param name="jellyfinItemId">The Jellyfin item.</param>
    /// <param name="provider">The provider that was searched.</param>
    /// <param name="title">The Jellyfin title, shown in the confirmation list.</param>
    /// <param name="year">The Jellyfin year, if known.</param>
    /// <param name="candidates">The candidates that were considered, best first.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// This is what keeps a weak match from being either silently dropped or wrongly trusted: the series
    /// stays off the calendar, but the administrator can see exactly which shows need a decision.
    /// </remarks>
    public async Task SavePendingMatchAsync(
        Guid jellyfinItemId,
        ReleaseProviderKind provider,
        string title,
        int? year,
        IReadOnlyList<ProviderSeries> candidates,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO pending_match
                (jellyfin_id, provider, title, year, candidates_json, updated_utc)
            VALUES ($jellyfin, $provider, $title, $year, $candidates, $updated)
            ON CONFLICT (jellyfin_id, provider) DO UPDATE SET
                title           = excluded.title,
                year            = excluded.year,
                candidates_json = excluded.candidates_json,
                updated_utc     = excluded.updated_utc;
            """;

        command.Parameters.AddWithValue("$jellyfin", jellyfinItemId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)provider);
        command.Parameters.AddWithValue("$title", title);
        command.Parameters.AddWithValue("$year", (object?)year ?? DBNull.Value);
        command.Parameters.AddWithValue("$candidates", JsonSerializer.Serialize(candidates, SerializerOptions));
        command.Parameters.AddWithValue("$updated", Iso(DateTime.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists the series awaiting a manual match decision.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The pending matches.</returns>
    public async Task<IReadOnlyList<PendingMatch>> GetPendingMatchesAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT jellyfin_id, provider, title, year, candidates_json, updated_utc
            FROM pending_match
            ORDER BY title;
            """;

        var results = new List<PendingMatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var candidates = JsonSerializer.Deserialize<List<ProviderSeries>>(reader.GetString(4), SerializerOptions)
                ?? [];

            results.Add(new PendingMatch(
                Guid.ParseExact(reader.GetString(0), "N"),
                (ReleaseProviderKind)reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                candidates,
                ParseIso(reader.GetString(5))));
        }

        return results;
    }

    /// <summary>
    /// Replaces the cached releases for one provider series.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <param name="providerId">The provider's series identifier.</param>
    /// <param name="releases">The releases to store.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SaveReleasesAsync(
        ReleaseProviderKind provider,
        string providerId,
        IReadOnlyList<ReleaseItem> releases,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(releases);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText =
                "DELETE FROM release_cache WHERE provider = $provider AND provider_id = $providerId;";
            delete.Parameters.AddWithValue("$provider", (int)provider);
            delete.Parameters.AddWithValue("$providerId", providerId);
            await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var now = Iso(DateTime.UtcNow);

        foreach (var release in releases)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT OR REPLACE INTO release_cache
                    (provider, provider_id, release_key, release_utc, payload_json, fetched_utc)
                VALUES ($provider, $providerId, $key, $releaseUtc, $payload, $fetched);
                """;

            insert.Parameters.AddWithValue("$provider", (int)provider);
            insert.Parameters.AddWithValue("$providerId", providerId);
            // The cache key keeps the sub/dub variant so that both airings of one episode survive;
            // merging them for display is the read path's job, not storage's.
            insert.Parameters.AddWithValue("$key", release.GetCacheKey());
            insert.Parameters.AddWithValue(
                "$releaseUtc",
                release.ReleaseUtc is { } date ? Iso(date) : (object)DBNull.Value);
            insert.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(release, SerializerOptions));
            insert.Parameters.AddWithValue("$fetched", now);

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads cached releases inside a date window.
    /// </summary>
    /// <param name="fromUtc">Inclusive window start.</param>
    /// <param name="toUtc">Inclusive window end.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The cached releases.</returns>
    /// <remarks>
    /// This is the read path the calendar uses. It never calls a provider, which is what lets the UI keep
    /// working — showing the last known data and when it was fetched — while a provider is unreachable.
    /// </remarks>
    public async Task<IReadOnlyList<ReleaseItem>> GetReleasesAsync(
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT payload_json
            FROM release_cache
            WHERE release_utc IS NOT NULL
              AND release_utc >= $from
              AND release_utc <= $to
            ORDER BY release_utc;
            """;

        command.Parameters.AddWithValue("$from", Iso(fromUtc));
        command.Parameters.AddWithValue("$to", Iso(toUtc));

        var results = new List<ReleaseItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var item = JsonSerializer.Deserialize<ReleaseItem>(reader.GetString(0), SerializerOptions);
            if (item is not null)
            {
                results.Add(item);
            }
        }

        return results;
    }

    /// <summary>
    /// Adds a followed series for a user.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="series">The series to follow.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task FollowAsync(Guid userId, ProviderSeries series, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(series);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT OR REPLACE INTO followed_item
                (user_id, provider, provider_id, title, is_anime, poster_url, added_utc)
            VALUES ($user, $provider, $providerId, $title, $anime, $poster, $added);
            """;

        command.Parameters.AddWithValue("$user", userId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)series.Provider);
        command.Parameters.AddWithValue("$providerId", series.ProviderId);
        command.Parameters.AddWithValue("$title", series.Title);
        command.Parameters.AddWithValue("$anime", series.IsAnime ? 1 : 0);
        command.Parameters.AddWithValue("$poster", (object?)series.PosterUrl ?? DBNull.Value);
        command.Parameters.AddWithValue("$added", Iso(DateTime.UtcNow));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Removes a followed series for a user.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <param name="provider">The provider.</param>
    /// <param name="providerId">The provider's series identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task UnfollowAsync(
        Guid userId,
        ReleaseProviderKind provider,
        string providerId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            DELETE FROM followed_item
            WHERE user_id = $user AND provider = $provider AND provider_id = $providerId;
            """;

        command.Parameters.AddWithValue("$user", userId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)provider);
        command.Parameters.AddWithValue("$providerId", providerId);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Lists a user's followed series.
    /// </summary>
    /// <param name="userId">The Jellyfin user, or <see cref="Guid.Empty"/> for every user.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The followed series.</returns>
    /// <remarks>
    /// The synchronization task passes <see cref="Guid.Empty"/> because it has to refresh what every
    /// user follows, while the API always scopes to the caller.
    /// </remarks>
    public async Task<IReadOnlyList<FollowedItem>> GetFollowedAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var scopeToUser = userId != Guid.Empty;

        command.CommandText = scopeToUser
            ? """
              SELECT user_id, provider, provider_id, title, is_anime, poster_url, added_utc
              FROM followed_item WHERE user_id = $user ORDER BY title;
              """
            : """
              SELECT user_id, provider, provider_id, title, is_anime, poster_url, added_utc
              FROM followed_item ORDER BY title;
              """;

        if (scopeToUser)
        {
            command.Parameters.AddWithValue("$user", userId.ToString("N"));
        }

        var results = new List<FollowedItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(new FollowedItem(
                Guid.ParseExact(reader.GetString(0), "N"),
                (ReleaseProviderKind)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetString(5),
                ParseIso(reader.GetString(6))));
        }

        return results;
    }

    /// <summary>
    /// Records when a synchronization last completed.
    /// </summary>
    /// <param name="whenUtc">The completion time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SetLastSyncAsync(DateTime whenUtc, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await SetMetaAsync(connection, "last_sync_utc", Iso(whenUtc), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Records the provider problems seen during the last synchronization.
    /// </summary>
    /// <param name="issues">Human-readable messages, already stripped of anything sensitive.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task SetLastSyncIssuesAsync(
        IReadOnlyList<string> issues,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issues);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await SetMetaAsync(
            connection,
            "last_sync_issues",
            JsonSerializer.Serialize(issues, SerializerOptions),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the provider problems seen during the last synchronization.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The messages, or an empty list.</returns>
    public async Task<IReadOnlyList<string>> GetLastSyncIssuesAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT value FROM meta WHERE key = 'last_sync_issues';";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

        if (string.IsNullOrEmpty(value))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<string>>(value, SerializerOptions) ?? [];
    }

    /// <summary>
    /// Reads when a synchronization last completed.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion time, or <see langword="null"/> if it has never run.</returns>
    public async Task<DateTime?> GetLastSyncAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT value FROM meta WHERE key = 'last_sync_utc';";
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;

        return value is null ? null : ParseIso(value);
    }

    /// <summary>
    /// Empties every cache table.
    /// </summary>
    /// <param name="includeFollows">Whether to also delete follows and manual mappings.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <remarks>
    /// Follows and hand-made mappings are user decisions, not cached provider data, so clearing the
    /// cache leaves them alone unless the caller explicitly asks otherwise.
    /// </remarks>
    public async Task ClearAsync(bool includeFollows, CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        command.CommandText = includeFollows
            ? """
              DELETE FROM release_cache;
              DELETE FROM series_cache;
              DELETE FROM pending_match;
              DELETE FROM provider_mapping;
              DELETE FROM followed_item;
              DELETE FROM meta WHERE key = 'last_sync_utc';
              """
            : """
              DELETE FROM release_cache;
              DELETE FROM series_cache;
              DELETE FROM pending_match;
              DELETE FROM provider_mapping WHERE is_manual = 0;
              DELETE FROM meta WHERE key = 'last_sync_utc';
              """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var vacuum = connection.CreateCommand();
        vacuum.CommandText = "VACUUM;";
        await vacuum.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("ReleaseHub cache cleared (follows removed: {Removed})", includeFollows);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _initGate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task SetMetaAsync(
        SqliteConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES ($key, $value);";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ClearPendingMatchAsync(
        SqliteConnection connection,
        Guid jellyfinItemId,
        ReleaseProviderKind provider,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            "DELETE FROM pending_match WHERE jellyfin_id = $jellyfin AND provider = $provider;";
        command.Parameters.AddWithValue("$jellyfin", jellyfinItemId.ToString("N"));
        command.Parameters.AddWithValue("$provider", (int)provider);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Formats a timestamp so that SQLite's lexicographic ordering matches chronological ordering.
    /// </summary>
    /// <param name="value">The timestamp.</param>
    /// <returns>A sortable ISO-8601 string in UTC.</returns>
    private static string Iso(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    private static DateTime ParseIso(string value)
        => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
}
