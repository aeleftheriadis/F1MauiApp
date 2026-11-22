using System.Globalization;
using Microsoft.Data.Sqlite;

namespace F1App.Telemetry.Providers;

/// <summary>
/// Keeps provider responses in SQLite so a second launch never asks the same question twice.
/// </summary>
/// <remarks>
/// <para>
/// The rows are the raw response bodies, keyed by the relative URL that produced them. That is the cheapest
/// thing that can work: the alternative - a table per endpoint - needs a schema and a mapper per endpoint,
/// while the JSON already has both, and a payload written today still parses when the DTO grows a field.
/// </para>
/// <para>
/// The point is the free tier: OpenF1 allows thirty requests a minute, so a screen that re-reads a finished
/// session costs budget for data that cannot have changed. Callers pass the age they tolerate; nothing here
/// decides freshness.
/// </para>
/// </remarks>
public sealed class SqlitePayloadCache
{
    private readonly string _connectionString;

    /// <summary>Creates the cache over <paramref name="databasePath"/>.</summary>
    public SqlitePayloadCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>
    /// How long a response stays useful, by what it contains.
    /// </summary>
    /// <remarks>
    /// A finished session cannot change, so its payloads are kept for a week and re-fetched only when that
    /// expires - far cheaper than trusting a timestamp in a payload. Calendar-shaped endpoints (sessions,
    /// meetings) do change, so they are kept for hours instead.
    /// </remarks>
    public static TimeSpan LifetimeFor(string relativeUrl)
    {
        ArgumentNullException.ThrowIfNull(relativeUrl);

        return relativeUrl.StartsWith("sessions", StringComparison.OrdinalIgnoreCase)
               || relativeUrl.StartsWith("meetings", StringComparison.OrdinalIgnoreCase)
            ? TimeSpan.FromHours(6)
            : TimeSpan.FromDays(7);
    }

    /// <summary>Returns the cached body for <paramref name="key"/>, or <see langword="null"/> when absent or stale.</summary>
    public async Task<string?> ReadAsync(
        string key,
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT body, fetched_at FROM payloads WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var fetchedAt = DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture);
        return now - fetchedAt > maxAge ? null : reader.GetString(0);
    }

    /// <summary>Stores the body for <paramref name="key"/>, replacing whatever was there.</summary>
    public async Task WriteAsync(
        string key,
        string body,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(body);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO payloads (key, body, fetched_at) VALUES ($key, $body, $fetchedAt)
            ON CONFLICT(key) DO UPDATE SET body = $body, fetched_at = $fetchedAt
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$fetchedAt", now.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Number of stored payloads; used by diagnostics and tests.</summary>
    public async Task<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM payloads";
        return Convert.ToInt64(
            await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS payloads (
                key        TEXT NOT NULL PRIMARY KEY,
                body       TEXT NOT NULL,
                fetched_at TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
