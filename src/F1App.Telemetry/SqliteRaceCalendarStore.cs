using System.Collections.Immutable;
using F1App.Domain;

namespace F1App.Telemetry;

/// <summary>
/// Calendar store backed by SQLite: read-through cache in front of the Jolpica provider.
/// </summary>
/// <remarks>
/// Hand-written SQL rather than an ORM. The table is a rebuildable cache with one shape and two
/// queries; an EF Core model would add startup cost and trim friction for no migration benefit.
/// Every statement is parameterised, and the connection is opened per call so the type has no
/// lifetime or thread-affinity contract to get wrong.
/// </remarks>
public sealed class SqliteRaceCalendarStore : IRaceCalendarStore
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;
    private readonly Providers.JolpicaClient _provider;

    /// <summary>Opens or creates the cache at <paramref name="databasePath"/> and reads through to <paramref name="provider"/>.</summary>
    public SqliteRaceCalendarStore(string databasePath, Providers.JolpicaClient provider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(provider);

        _connectionString = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();

        _provider = provider;
    }

    /// <inheritdoc />
    public async Task<FetchResult<ImmutableArray<RaceWeekend>>> GetSeasonAsync(
        int season,
        bool refresh,
        CancellationToken cancellationToken)
    {
        if (!refresh)
        {
            var cached = await ReadAsync(season, cancellationToken).ConfigureAwait(false);
            if (cached.Length > 0)
            {
                return cached;
            }
        }

        var fetched = await _provider.GetSeasonAsync(season, cancellationToken).ConfigureAwait(false);

        switch (fetched)
        {
            case ImmutableArray<RaceWeekend> races:
                if (races.Length > 0)
                {
                    await WriteAsync(races, cancellationToken).ConfigureAwait(false);
                }

                return races;

            case HttpFailure failure:
                // Offline with a stale cache is still better than an empty calendar.
                var stale = await ReadAsync(season, cancellationToken).ConfigureAwait(false);
                return stale.Length > 0 ? stale : failure;

            case RateLimited limited:
                return limited;

            default:
                return new HttpFailure(0, "unexpected provider result");
        }
    }

    private async Task<ImmutableArray<RaceWeekend>> ReadAsync(int season, CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT meeting_key, round, circuit_id, circuit_short, circuit_name, country, name, race_start
            FROM races
            WHERE season = $season
            ORDER BY round
            """;
        command.Parameters.AddWithValue("$season", season);

        var builder = ImmutableArray.CreateBuilder<RaceWeekend>(24);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            builder.Add(new RaceWeekend(
                MeetingKey: reader.GetInt32(0),
                Season: season,
                Round: reader.GetInt32(1),
                Circuit: new Circuit(
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5)),
                Name: reader.GetString(6),
                RaceStart: DateTimeOffset.Parse(
                    reader.GetString(7),
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        return builder.ToImmutable();
    }

    private async Task WriteAsync(ImmutableArray<RaceWeekend> races, CancellationToken cancellationToken)
    {
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.Transaction = (Microsoft.Data.Sqlite.SqliteTransaction)transaction;
        command.CommandText =
            """
            INSERT INTO races
                (season, round, meeting_key, circuit_id, circuit_short, circuit_name, country, name, race_start)
            VALUES
                ($season, $round, $meetingKey, $circuitId, $circuitShort, $circuitName, $country, $name, $raceStart)
            ON CONFLICT(season, round) DO UPDATE SET
                meeting_key = excluded.meeting_key,
                circuit_id = excluded.circuit_id,
                circuit_short = excluded.circuit_short,
                circuit_name = excluded.circuit_name,
                country = excluded.country,
                name = excluded.name,
                race_start = excluded.race_start
            """;

        var season = command.Parameters.Add("$season", Microsoft.Data.Sqlite.SqliteType.Integer);
        var round = command.Parameters.Add("$round", Microsoft.Data.Sqlite.SqliteType.Integer);
        var meetingKey = command.Parameters.Add("$meetingKey", Microsoft.Data.Sqlite.SqliteType.Integer);
        var circuitId = command.Parameters.Add("$circuitId", Microsoft.Data.Sqlite.SqliteType.Text);
        var circuitShort = command.Parameters.Add("$circuitShort", Microsoft.Data.Sqlite.SqliteType.Text);
        var circuitName = command.Parameters.Add("$circuitName", Microsoft.Data.Sqlite.SqliteType.Text);
        var country = command.Parameters.Add("$country", Microsoft.Data.Sqlite.SqliteType.Text);
        var name = command.Parameters.Add("$name", Microsoft.Data.Sqlite.SqliteType.Text);
        var raceStart = command.Parameters.Add("$raceStart", Microsoft.Data.Sqlite.SqliteType.Text);

        foreach (var race in races)
        {
            season.Value = race.Season;
            round.Value = race.Round;
            meetingKey.Value = race.MeetingKey;
            circuitId.Value = race.Circuit.Id;
            circuitShort.Value = race.Circuit.ShortName;
            circuitName.Value = race.Circuit.Name;
            country.Value = race.Circuit.Country;
            name.Value = race.Name;
            raceStart.Value = race.RaceStart.ToString("O", System.Globalization.CultureInfo.InvariantCulture);

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnsureSchemaAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS races (
                 season        INTEGER NOT NULL,
                 round         INTEGER NOT NULL,
                 meeting_key   INTEGER NOT NULL,
                 circuit_id    TEXT    NOT NULL,
                 circuit_short TEXT    NOT NULL,
                 circuit_name  TEXT    NOT NULL,
                 country       TEXT    NOT NULL,
                 name          TEXT    NOT NULL,
                 race_start    TEXT    NOT NULL,
                 PRIMARY KEY (season, round)
             );
             PRAGMA user_version = {SchemaVersion};
             """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
