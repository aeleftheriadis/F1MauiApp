using System.Collections.Immutable;
using System.Globalization;
using F1App.Domain;
using Microsoft.Data.Sqlite;

namespace F1App.Telemetry.Facts;

/// <summary>
/// SQLite persistence for the facts screens, so "offline" survives a restart.
/// </summary>
/// <remarks>
/// Same database file as the calendar cache, two more tables, hand-written SQL. Rows carry a fetch
/// timestamp: a cached list older than the caller's TTL is treated as absent, so the service can decide
/// freshness without a second source of truth.
/// </remarks>
public sealed class SqliteFactsCache
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;

    /// <summary>Creates the cache over <paramref name="databasePath"/>.</summary>
    public SqliteFactsCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Reads the cached champions, or an empty list when stale or absent.</summary>
    public async Task<ImmutableArray<SeasonChampion>> ReadChampionsAsync(
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT season, driver, constructor, points, wins
            FROM champions
            WHERE fetched_at >= $since
            ORDER BY season
            """;
        command.Parameters.AddWithValue("$since", (now - maxAge).ToString("O", CultureInfo.InvariantCulture));

        var builder = ImmutableArray.CreateBuilder<SeasonChampion>(20);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new SeasonChampion(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetInt32(4)));
        }

        return builder.ToImmutable();
    }

    /// <summary>Replaces the cached champions.</summary>
    public async Task WriteChampionsAsync(
        ImmutableArray<SeasonChampion> champions,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (champions.IsDefaultOrEmpty)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM champions";
        await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO champions (season, driver, constructor, points, wins, fetched_at)
            VALUES ($season, $driver, $constructor, $points, $wins, $fetchedAt)
            """;

        var season = insert.Parameters.Add("$season", SqliteType.Integer);
        var driver = insert.Parameters.Add("$driver", SqliteType.Text);
        var constructor = insert.Parameters.Add("$constructor", SqliteType.Text);
        var points = insert.Parameters.Add("$points", SqliteType.Real);
        var wins = insert.Parameters.Add("$wins", SqliteType.Integer);
        var fetchedAt = insert.Parameters.Add("$fetchedAt", SqliteType.Text);
        fetchedAt.Value = now.ToString("O", CultureInfo.InvariantCulture);

        foreach (var champion in champions)
        {
            season.Value = champion.Season;
            driver.Value = champion.DriverName;
            constructor.Value = champion.ConstructorName;
            points.Value = champion.Points;
            wins.Value = champion.Wins;

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads cached winners for one circuit, or an empty list when stale or absent.</summary>
    public async Task<ImmutableArray<CircuitWinner>> ReadWinnersAsync(
        string circuitId,
        DateTimeOffset now,
        TimeSpan maxAge,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT driver, wins, last_season
            FROM circuit_winners
            WHERE circuit_id = $circuit AND fetched_at >= $since
            ORDER BY wins DESC, last_season DESC
            """;
        command.Parameters.AddWithValue("$circuit", circuitId);
        command.Parameters.AddWithValue("$since", (now - maxAge).ToString("O", CultureInfo.InvariantCulture));

        var builder = ImmutableArray.CreateBuilder<CircuitWinner>(10);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new CircuitWinner(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2)));
        }

        return builder.ToImmutable();
    }

    /// <summary>Replaces the cached winners for one circuit.</summary>
    public async Task WriteWinnersAsync(
        string circuitId,
        ImmutableArray<CircuitWinner> winners,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(circuitId);

        if (winners.IsDefaultOrEmpty)
        {
            return;
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM circuit_winners WHERE circuit_id = $circuit";
        clear.Parameters.AddWithValue("$circuit", circuitId);
        await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO circuit_winners (circuit_id, driver, wins, last_season, fetched_at)
            VALUES ($circuit, $driver, $wins, $lastSeason, $fetchedAt)
            """;

        var circuit = insert.Parameters.Add("$circuit", SqliteType.Text);
        var driver = insert.Parameters.Add("$driver", SqliteType.Text);
        var wins = insert.Parameters.Add("$wins", SqliteType.Integer);
        var lastSeason = insert.Parameters.Add("$lastSeason", SqliteType.Integer);
        var fetchedAt = insert.Parameters.Add("$fetchedAt", SqliteType.Text);

        circuit.Value = circuitId;
        fetchedAt.Value = now.ToString("O", CultureInfo.InvariantCulture);

        foreach (var winner in winners)
        {
            driver.Value = winner.DriverName;
            wins.Value = winner.Wins;
            lastSeason.Value = winner.LastSeason;

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS champions (
                 season      INTEGER NOT NULL PRIMARY KEY,
                 driver      TEXT    NOT NULL,
                 constructor TEXT    NOT NULL,
                 points      REAL    NOT NULL,
                 wins        INTEGER NOT NULL,
                 fetched_at  TEXT    NOT NULL
             );
             CREATE TABLE IF NOT EXISTS circuit_winners (
                 circuit_id  TEXT    NOT NULL,
                 driver      TEXT    NOT NULL,
                 wins        INTEGER NOT NULL,
                 last_season INTEGER NOT NULL,
                 fetched_at  TEXT    NOT NULL,
                 PRIMARY KEY (circuit_id, driver)
             );
             PRAGMA user_version = {SchemaVersion};
             """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
