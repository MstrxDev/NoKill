using Microsoft.Data.Sqlite;

namespace NoKill.Vault;

/// <summary>One row of freeze history. EndedAt is null while the incident is (or was last seen) ongoing.</summary>
public sealed record FreezeIncidentRecord
{
    public required long Id { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public required string ProcessName { get; init; }

    public required int ProcessId { get; init; }

    public string? ExecutablePath { get; init; }

    /// <summary>"manual" (user clicked/ran preserve) or "watchdog" (auto-detected).</summary>
    public required string Trigger { get; init; }

    public string? VaultEntryPath { get; init; }

    /// <summary>Top diagnostic insight at capture time (wait-chain summary etc.).</summary>
    public string? Insight { get; init; }

    /// <summary>How long the freeze lasted, when the end was observed (watchdog incidents).</summary>
    public TimeSpan? Duration => EndedAt - StartedAt;
}

/// <summary>Aggregate view: which apps freeze the most.</summary>
public sealed record FreezeOffender(string ProcessName, int IncidentCount, DateTimeOffset LastIncidentAt);

/// <summary>
/// Local SQLite log of every freeze incident NoKill has seen — the memory
/// that turns one-off rescues into patterns ("Blender has frozen 9 times
/// this month, always around autosave"). Local-first like everything else:
/// one .db file next to the vault, no telemetry, no cloud.
/// </summary>
public sealed class FreezeHistory
{
    private readonly string _connectionString;

    public FreezeHistory(string? databasePath = null)
    {
        DatabasePath = databasePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "NoKill", "history.db");

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString();
        EnsureSchema();
    }

    public string DatabasePath { get; }

    public long RecordIncident(
        string processName,
        int processId,
        string? executablePath,
        string trigger,
        string? vaultEntryPath,
        string? insight,
        DateTimeOffset? startedAt = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO incidents (StartedAt, ProcessName, ProcessId, ExecutablePath, Trigger, VaultEntryPath, Insight)
            VALUES ($started, $name, $pid, $exe, $trigger, $vault, $insight);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$started", (startedAt ?? DateTimeOffset.Now).ToString("O"));
        command.Parameters.AddWithValue("$name", processName);
        command.Parameters.AddWithValue("$pid", processId);
        command.Parameters.AddWithValue("$exe", (object?)executablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$trigger", trigger);
        command.Parameters.AddWithValue("$vault", (object?)vaultEntryPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$insight", (object?)insight ?? DBNull.Value);

        return (long)command.ExecuteScalar()!;
    }

    public void MarkEnded(long incidentId, DateTimeOffset? endedAt = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE incidents SET EndedAt = $ended WHERE Id = $id AND EndedAt IS NULL";
        command.Parameters.AddWithValue("$ended", (endedAt ?? DateTimeOffset.Now).ToString("O"));
        command.Parameters.AddWithValue("$id", incidentId);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<FreezeIncidentRecord> GetRecent(int count = 50) =>
        Query("SELECT * FROM incidents ORDER BY StartedAt DESC, Id DESC LIMIT $limit",
            c => c.Parameters.AddWithValue("$limit", count));

    public IReadOnlyList<FreezeIncidentRecord> GetForProcess(string processName, int count = 50) =>
        Query("SELECT * FROM incidents WHERE ProcessName = $name COLLATE NOCASE " +
              "ORDER BY StartedAt DESC, Id DESC LIMIT $limit",
            c =>
            {
                c.Parameters.AddWithValue("$name", processName);
                c.Parameters.AddWithValue("$limit", count);
            });

    /// <summary>Which processes freeze the most, most frequent first.</summary>
    public IReadOnlyList<FreezeOffender> GetTopOffenders(int count = 10)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProcessName, COUNT(*) AS Incidents, MAX(StartedAt) AS LastAt
            FROM incidents
            GROUP BY ProcessName COLLATE NOCASE
            ORDER BY Incidents DESC, LastAt DESC
            LIMIT $limit
            """;
        command.Parameters.AddWithValue("$limit", count);

        var offenders = new List<FreezeOffender>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            offenders.Add(new FreezeOffender(
                reader.GetString(0),
                reader.GetInt32(1),
                DateTimeOffset.Parse(reader.GetString(2))));
        }

        return offenders;
    }

    private IReadOnlyList<FreezeIncidentRecord> Query(string sql, Action<SqliteCommand> bind)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        bind(command);

        var records = new List<FreezeIncidentRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            records.Add(new FreezeIncidentRecord
            {
                Id = reader.GetInt64(reader.GetOrdinal("Id")),
                StartedAt = DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("StartedAt"))),
                EndedAt = reader.IsDBNull(reader.GetOrdinal("EndedAt"))
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("EndedAt"))),
                ProcessName = reader.GetString(reader.GetOrdinal("ProcessName")),
                ProcessId = reader.GetInt32(reader.GetOrdinal("ProcessId")),
                ExecutablePath = reader.IsDBNull(reader.GetOrdinal("ExecutablePath"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("ExecutablePath")),
                Trigger = reader.GetString(reader.GetOrdinal("Trigger")),
                VaultEntryPath = reader.IsDBNull(reader.GetOrdinal("VaultEntryPath"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("VaultEntryPath")),
                Insight = reader.IsDBNull(reader.GetOrdinal("Insight"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("Insight")),
            });
        }

        return records;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private void EnsureSchema()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS incidents (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StartedAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                ProcessName TEXT NOT NULL,
                ProcessId INTEGER NOT NULL,
                ExecutablePath TEXT NULL,
                Trigger TEXT NOT NULL,
                VaultEntryPath TEXT NULL,
                Insight TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_incidents_process ON incidents (ProcessName);
            CREATE INDEX IF NOT EXISTS idx_incidents_started ON incidents (StartedAt);
            """;
        command.ExecuteNonQuery();
    }
}
