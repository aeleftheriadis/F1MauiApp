using System.Globalization;
using Microsoft.Data.Sqlite;

namespace F1App.Telemetry.Media;

/// <summary>
/// Remembers which picture was found for a subject, so Commons is queried once.
/// </summary>
/// <remarks>
/// Commons asks clients to be conservative; it is also a public service run by donations. Caching the
/// resolved URL and its attribution means a driver's photo costs one request ever, and the panel still
/// works with no network. A negative result is cached too, so a subject with no free photo is not
/// re-queried on every visit.
/// </remarks>
public sealed class SqliteMediaCache
{
    private const int SchemaVersion = 1;

    private readonly string _connectionString;

    /// <summary>Creates the cache over <paramref name="databasePath"/>.</summary>
    public SqliteMediaCache(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary>Reads a cached image, or <see langword="null"/> when the subject has never been looked up.</summary>
    public async Task<OpenImage?> ReadAsync(string subject, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT title, preview, page, licence, artist
            FROM media
            WHERE subject = $subject
            """;
        command.Parameters.AddWithValue("$subject", subject);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // An empty title marks a cached "nothing found".
        return reader.GetString(0) is { Length: > 0 } title
            ? new OpenImage(title, reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))
            : Empty;
    }

    /// <summary>Records the result of a lookup, including a miss.</summary>
    public async Task WriteAsync(
        string subject,
        OpenImage? image,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureSchemaAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO media (subject, title, preview, page, licence, artist, fetched_at)
            VALUES ($subject, $title, $preview, $page, $licence, $artist, $fetchedAt)
            ON CONFLICT(subject) DO UPDATE SET
                title = excluded.title,
                preview = excluded.preview,
                page = excluded.page,
                licence = excluded.licence,
                artist = excluded.artist,
                fetched_at = excluded.fetched_at
            """;

        command.Parameters.AddWithValue("$subject", subject);
        command.Parameters.AddWithValue("$title", image?.Title ?? string.Empty);
        command.Parameters.AddWithValue("$preview", image?.PreviewUrl ?? string.Empty);
        command.Parameters.AddWithValue("$page", image?.FilePageUrl ?? string.Empty);
        command.Parameters.AddWithValue("$licence", image?.Licence ?? string.Empty);
        command.Parameters.AddWithValue("$artist", image?.Artist ?? string.Empty);
        command.Parameters.AddWithValue("$fetchedAt", now.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>A cached "looked up, nothing free exists" marker.</summary>
    public static OpenImage Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);

    private static async Task EnsureSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             CREATE TABLE IF NOT EXISTS media (
                 subject    TEXT NOT NULL PRIMARY KEY,
                 title      TEXT NOT NULL,
                 preview    TEXT NOT NULL,
                 page       TEXT NOT NULL,
                 licence    TEXT NOT NULL,
                 artist     TEXT NOT NULL,
                 fetched_at TEXT NOT NULL
             );
             PRAGMA user_version = {SchemaVersion};
             """;

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
