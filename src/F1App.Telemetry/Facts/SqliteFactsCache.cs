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
    private const int SchemaVersion = 4;

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

    /// <summary>Every archived race in the vault, oldest first.</summary>
    public async Task<ImmutableArray<ArchivedRace>> ReadRacesAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT season, round, race_name, circuit_id, circuit_name, country, date, winner, constructor, laps, winning_time
            FROM race_vault
            ORDER BY season, round
            """;

        var builder = ImmutableArray.CreateBuilder<ArchivedRace>(1200);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new ArchivedRace(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                DateOnly.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                reader.GetString(7),
                reader.GetString(8),
                reader.GetInt32(9),
                reader.GetString(10)));
        }

        return builder.ToImmutable();
    }

    /// <summary>Every season held in the vault, with when its rows were fetched.</summary>
    public async Task<ImmutableArray<(int Season, DateTimeOffset FetchedAt)>> ReadSeasonsAsync(
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT season, MAX(fetched_at)
            FROM race_vault
            GROUP BY season
            ORDER BY season
            """;

        var builder = ImmutableArray.CreateBuilder<(int, DateTimeOffset)>(80);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add((
                reader.GetInt32(0),
                DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture)));
        }

        return builder.ToImmutable();
    }

    /// <summary>Replaces the archived races of one season. An empty list clears the season.</summary>
    public async Task WriteRacesAsync(
        int season,
        ImmutableArray<ArchivedRace> races,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM race_vault WHERE season = $season";
        clear.Parameters.AddWithValue("$season", season);
        await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO race_vault (
                season, round, race_name, circuit_id, circuit_name, country,
                date, winner, constructor, laps, winning_time, fetched_at)
            VALUES (
                $season, $round, $raceName, $circuitId, $circuitName, $country,
                $date, $winner, $constructor, $laps, $winningTime, $fetchedAt)
            """;

        var round = insert.Parameters.Add("$round", SqliteType.Integer);
        var raceName = insert.Parameters.Add("$raceName", SqliteType.Text);
        var circuitId = insert.Parameters.Add("$circuitId", SqliteType.Text);
        var circuitName = insert.Parameters.Add("$circuitName", SqliteType.Text);
        var country = insert.Parameters.Add("$country", SqliteType.Text);
        var date = insert.Parameters.Add("$date", SqliteType.Text);
        var winner = insert.Parameters.Add("$winner", SqliteType.Text);
        var constructor = insert.Parameters.Add("$constructor", SqliteType.Text);
        var laps = insert.Parameters.Add("$laps", SqliteType.Integer);
        var winningTime = insert.Parameters.Add("$winningTime", SqliteType.Text);
        var fetchedAt = insert.Parameters.Add("$fetchedAt", SqliteType.Text);

        insert.Parameters.AddWithValue("$season", season);
        fetchedAt.Value = now.ToString("O", CultureInfo.InvariantCulture);

        foreach (var race in races)
        {
            round.Value = race.Round;
            raceName.Value = race.RaceName;
            circuitId.Value = race.CircuitId;
            circuitName.Value = race.CircuitName;
            country.Value = race.Country;
            date.Value = race.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            winner.Value = race.Winner;
            constructor.Value = race.Constructor;
            laps.Value = race.Laps;
            winningTime.Value = race.WinningTime;

            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Every cached classification row of one season.</summary>
    public async Task<ImmutableArray<RaceResultRow>> ReadSeasonResultsAsync(
        int season,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT season, round, driver_id, driver_code, driver_name, constructor_id,
                   position, position_text, grid, status
            FROM season_results
            WHERE season = $season
            ORDER BY round, position
            """;
        command.Parameters.AddWithValue("$season", season);

        var builder = ImmutableArray.CreateBuilder<RaceResultRow>(512);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new RaceResultRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.GetString(9)));
        }

        return builder.ToImmutable();
    }

    /// <summary>When one season's classifications were last written, or <see langword="null"/> when never.</summary>
    public async Task<DateTimeOffset?> ReadSeasonResultsFetchedAtAsync(
        int season,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(fetched_at) FROM season_results WHERE season = $season";
        command.Parameters.AddWithValue("$season", season);

        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return value is string text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>Replaces one season's classifications.</summary>
    public async Task WriteSeasonResultsAsync(
        int season,
        ImmutableArray<RaceResultRow> rows,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var clear = connection.CreateCommand();
        clear.Transaction = transaction;
        clear.CommandText = "DELETE FROM season_results WHERE season = $season";
        clear.Parameters.AddWithValue("$season", season);
        await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO season_results (
                season, round, driver_id, driver_code, driver_name, constructor_id,
                position, position_text, grid, status, fetched_at)
            VALUES (
                $season, $round, $driverId, $driverCode, $driverName, $constructorId,
                $position, $positionText, $grid, $status, $fetchedAt)
            """;

        var round = insert.Parameters.Add("$round", SqliteType.Integer);
        var driverId = insert.Parameters.Add("$driverId", SqliteType.Text);
        var driverCode = insert.Parameters.Add("$driverCode", SqliteType.Text);
        var driverName = insert.Parameters.Add("$driverName", SqliteType.Text);
        var constructorId = insert.Parameters.Add("$constructorId", SqliteType.Text);
        var position = insert.Parameters.Add("$position", SqliteType.Integer);
        var positionText = insert.Parameters.Add("$positionText", SqliteType.Text);
        var grid = insert.Parameters.Add("$grid", SqliteType.Integer);
        var status = insert.Parameters.Add("$status", SqliteType.Text);
        insert.Parameters.AddWithValue("$season", season);
        insert.Parameters.AddWithValue("$fetchedAt", now.ToString("O", CultureInfo.InvariantCulture));

        foreach (var row in rows)
        {
            round.Value = row.Round;
            driverId.Value = row.DriverId;
            driverCode.Value = row.DriverCode;
            driverName.Value = row.DriverName;
            constructorId.Value = row.ConstructorId;
            position.Value = (object?)row.Position ?? DBNull.Value;
            positionText.Value = row.PositionText;
            grid.Value = (object?)row.Grid ?? DBNull.Value;
            status.Value = row.Status;

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
             CREATE TABLE IF NOT EXISTS race_vault (
                 season       INTEGER NOT NULL,
                 round        INTEGER NOT NULL,
                 race_name    TEXT    NOT NULL,
                 circuit_id   TEXT    NOT NULL,
                 circuit_name TEXT    NOT NULL,
                 country      TEXT    NOT NULL,
                 date         TEXT    NOT NULL,
                 winner       TEXT    NOT NULL,
                 constructor  TEXT    NOT NULL,
                 laps         INTEGER NOT NULL,
                 winning_time TEXT    NOT NULL,
                 fetched_at   TEXT    NOT NULL,
                 PRIMARY KEY (season, round)
             );
             CREATE TABLE IF NOT EXISTS season_results (
                 season        INTEGER NOT NULL,
                 round         INTEGER NOT NULL,
                 driver_id     TEXT    NOT NULL,
                 driver_code   TEXT    NOT NULL,
                 driver_name   TEXT    NOT NULL,
                 constructor_id TEXT   NOT NULL,
                 position      INTEGER,
                 position_text TEXT    NOT NULL,
                 grid          INTEGER,
                 status        TEXT    NOT NULL,
                 fetched_at    TEXT    NOT NULL,
                 PRIMARY KEY (season, round, driver_id)
             );
             """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await MigrateAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies data corrections and stamps the version. Runs once per database.</summary>
    private static async Task MigrateAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version";
        var current = Convert.ToInt32(
            await version.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false),
            CultureInfo.InvariantCulture);

        if (current >= SchemaVersion)
        {
            return;
        }

        // v3: the Indianapolis 500 was championship-scoring until 1960 but is not a Grand Prix.
        await using var purge = connection.CreateCommand();
        purge.CommandText = "DELETE FROM race_vault WHERE circuit_id = 'indianapolis' AND season <= 1960";
        await purge.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var stamp = connection.CreateCommand();
        stamp.CommandText = $"PRAGMA user_version = {SchemaVersion}";
        await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
