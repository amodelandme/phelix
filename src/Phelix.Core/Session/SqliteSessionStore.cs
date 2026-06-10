using System.Text.Json;
using Microsoft.Data.Sqlite;
using Phelix.Core.Agent;

namespace Phelix.Core.Session;

/// <summary>
/// <see cref="ISessionStore"/> backed by a per-session SQLite database.
/// </summary>
/// <remarks>
/// The database file lives at <c>~/.phelix/sessions/&lt;sessionId&gt;.db</c>,
/// matching the naming convention of the existing JSONL session log.
/// When <paramref name="connectionString"/> is <c>:memory:</c> the store operates
/// entirely in memory — used by tests to avoid file I/O.
/// <para>
/// Schema: two tables. <c>turns</c> stores one row per <see cref="TurnRecord"/>,
/// with <c>tool_calls</c> persisted as a JSON text column.
/// <c>tool_outputs</c> is an FTS5 virtual table — one row per
/// <see cref="ToolCallRecord"/>, extracted on write and indexed for full-text search.
/// </para>
/// <para>
/// Every <see cref="AppendAsync"/> call writes both tables inside a single
/// transaction so the two writes are atomic — either both land or neither does.
/// </para>
/// </remarks>
public sealed class SqliteSessionStore : ISessionStore, IDisposable
{
    readonly SqliteConnection _connection;

    /// <summary>
    /// Opens (or creates) the session database at the path derived from <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// The session identity. Determines the database file path:
    /// <c>~/.phelix/sessions/&lt;fileSlug&gt;.db</c>.
    /// Pass a <see cref="SessionContext"/> with <c>SessionId == ":memory:"</c> to run entirely
    /// in memory (test use only).
    /// </param>
    public SqliteSessionStore(SessionContext context)
    {
        string connectionString = context.SessionId == ":memory:"
            ? "Data Source=:memory:"
            : BuildConnectionString(context);

        _connection = new SqliteConnection(connectionString);
        _connection.Open();

        EnsureSchema();
    }

    /// <inheritdoc/>
    public async Task AppendAsync(TurnRecord record, CancellationToken cancellationToken = default)
    {
        await using SqliteTransaction transaction = _connection.BeginTransaction();

        string toolCallsJson = JsonSerializer.Serialize(record.ToolCalls);

        await using SqliteCommand insertTurn = _connection.CreateCommand();
        insertTurn.Transaction = transaction;
        insertTurn.CommandText = """
            INSERT INTO turns
                (turn_id, session_id, session_name, user_message, final_assistant_message,
                 model_id, started_at, completed_at, exit_reason,
                 input_tokens, output_tokens, tool_calls_json)
            VALUES
                ($turnId, $sessionId, $sessionName, $userMessage, $finalAssistantMessage,
                 $modelId, $startedAt, $completedAt, $exitReason,
                 $inputTokens, $outputTokens, $toolCallsJson)
            """;

        insertTurn.Parameters.AddWithValue("$turnId", record.TurnId);
        insertTurn.Parameters.AddWithValue("$sessionId", record.SessionId);
        insertTurn.Parameters.AddWithValue("$sessionName", (object?)record.SessionName ?? DBNull.Value);
        insertTurn.Parameters.AddWithValue("$userMessage", record.UserMessage);
        insertTurn.Parameters.AddWithValue("$finalAssistantMessage", record.FinalAssistantMessage);
        insertTurn.Parameters.AddWithValue("$modelId", record.ModelId);
        insertTurn.Parameters.AddWithValue("$startedAt", record.StartedAt.ToUnixTimeMilliseconds());
        insertTurn.Parameters.AddWithValue("$completedAt", record.CompletedAt.ToUnixTimeMilliseconds());
        insertTurn.Parameters.AddWithValue("$exitReason", record.ExitReason.ToString());
        insertTurn.Parameters.AddWithValue("$inputTokens", record.Usage.InputTokens);
        insertTurn.Parameters.AddWithValue("$outputTokens", record.Usage.OutputTokens);
        insertTurn.Parameters.AddWithValue("$toolCallsJson", toolCallsJson);

        await insertTurn.ExecuteNonQueryAsync(cancellationToken);

        if (record.ToolCalls.Count > 0)
        {
            await using SqliteCommand insertOutput = _connection.CreateCommand();
            insertOutput.Transaction = transaction;
            insertOutput.CommandText = """
                INSERT INTO tool_outputs
                    (turn_id, session_id, tool_name, arguments_json, result)
                VALUES
                    ($turnId, $sessionId, $toolName, $argumentsJson, $result)
                """;

            SqliteParameter pTurnId     = insertOutput.Parameters.Add("$turnId",        SqliteType.Text);
            SqliteParameter pSessionId  = insertOutput.Parameters.Add("$sessionId",     SqliteType.Text);
            SqliteParameter pToolName   = insertOutput.Parameters.Add("$toolName",      SqliteType.Text);
            SqliteParameter pArgJson    = insertOutput.Parameters.Add("$argumentsJson", SqliteType.Text);
            SqliteParameter pResult     = insertOutput.Parameters.Add("$result",        SqliteType.Text);

            foreach (ToolCallRecord toolCall in record.ToolCalls)
            {
                pTurnId.Value    = record.TurnId;
                pSessionId.Value = record.SessionId;
                pToolName.Value  = toolCall.Name;
                pArgJson.Value   = toolCall.ArgumentsJson;
                pResult.Value    = toolCall.Result;
                await insertOutput.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<TurnRecord>> GetTurnsAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT turn_id, session_id, session_name, user_message, final_assistant_message,
                   model_id, started_at, completed_at, exit_reason,
                   input_tokens, output_tokens, tool_calls_json
            FROM turns
            WHERE session_id = $sessionId
            ORDER BY started_at ASC
            """;

        command.Parameters.AddWithValue("$sessionId", sessionId);

        List<TurnRecord> turns = [];

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            string toolCallsJson = reader.GetString(11);
            IReadOnlyList<ToolCallRecord> toolCalls =
                JsonSerializer.Deserialize<IReadOnlyList<ToolCallRecord>>(toolCallsJson)
                ?? [];

            TurnRecord turn = new(
                TurnId: reader.GetString(0),
                SessionId: reader.GetString(1),
                SessionName: reader.IsDBNull(2) ? null : reader.GetString(2),
                UserMessage: reader.GetString(3),
                FinalAssistantMessage: reader.GetString(4),
                ModelId: reader.GetString(5),
                StartedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(6)),
                CompletedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(7)),
                ExitReason: Enum.Parse<TurnExitReason>(reader.GetString(8)),
                Usage: new UsageSummary(reader.GetInt32(9), reader.GetInt32(10)),
                ToolCalls: toolCalls
            );

            turns.Add(turn);
        }

        return turns;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolCallRecord>> SearchToolOutputsAsync(
        string query,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        string sanitizedQuery = SanitizeFtsQuery(query);

        if (string.IsNullOrWhiteSpace(sanitizedQuery))
            return [];

        await using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = """
            SELECT tool_name, arguments_json, result
            FROM tool_outputs
            WHERE tool_outputs MATCH $query
            ORDER BY rank
            LIMIT $maxResults
            """;

        command.Parameters.AddWithValue("$query", sanitizedQuery);
        command.Parameters.AddWithValue("$maxResults", maxResults);

        List<ToolCallRecord> results = [];

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            ToolCallRecord record = new(
                CallId: string.Empty,
                Name: reader.GetString(0),
                ArgumentsJson: reader.GetString(1),
                Result: reader.GetString(2),
                Status: ToolCallStatus.Succeeded
            );

            results.Add(record);
        }

        return results;
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Strips characters that are syntactically significant to FTS5 so that
    /// free-form model-generated queries do not produce parse errors.
    /// </summary>
    /// <remarks>
    /// FTS5 treats '.', '"', '*', '(', ')', '-', '^', and ':' as query operators.
    /// A query like "AGENTS.md" produces "fts5: syntax error near '.'" because the
    /// dot splits the token mid-parse. Keeping only letters, digits, and whitespace
    /// reduces any query to a plain multi-term match, which is all we need here.
    /// </remarks>
    static string SanitizeFtsQuery(string query)
    {
        System.Text.StringBuilder sanitized = new(query.Length);

        foreach (char character in query)
        {
            if (char.IsLetterOrDigit(character) || char.IsWhiteSpace(character))
                sanitized.Append(character);
            else
                sanitized.Append(' ');
        }

        return sanitized.ToString().Trim();
    }

    static string BuildConnectionString(SessionContext context)
    {
        string sessionDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".phelix",
            "sessions");

        Directory.CreateDirectory(sessionDirectory);

        string databasePath = Path.Combine(sessionDirectory, $"{context.FileSlug}.db");

        return $"Data Source={databasePath}";
    }

    void EnsureSchema()
    {
        SqliteCommand createTurns = _connection.CreateCommand();
        createTurns.CommandText = """
            CREATE TABLE IF NOT EXISTS turns (
                turn_id                  TEXT NOT NULL PRIMARY KEY,
                session_id               TEXT NOT NULL,
                session_name             TEXT,
                user_message             TEXT NOT NULL,
                final_assistant_message  TEXT NOT NULL,
                model_id                 TEXT NOT NULL,
                started_at               INTEGER NOT NULL,
                completed_at             INTEGER NOT NULL,
                exit_reason              TEXT NOT NULL,
                input_tokens             INTEGER NOT NULL,
                output_tokens            INTEGER NOT NULL,
                tool_calls_json          TEXT NOT NULL
            )
            """;
        createTurns.ExecuteNonQuery();

        SqliteCommand createToolOutputs = _connection.CreateCommand();
        createToolOutputs.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS tool_outputs USING fts5(
                turn_id,
                session_id,
                tool_name,
                arguments_json,
                result
            )
            """;
        createToolOutputs.ExecuteNonQuery();
    }
}
