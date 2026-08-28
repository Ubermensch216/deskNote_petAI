using Dapper;
using DeskNote.Core.Abstractions;

namespace DeskNote.Data;

/// <inheritdoc cref="ISettingsStore"/>
public sealed class SqliteSettingsStore(SqliteConnectionFactory connectionFactory) : ISettingsStore
{
    public async Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.ExecuteScalarAsync<string?>(
            new CommandDefinition(
                "SELECT value FROM settings WHERE key = @key;",
                new { key },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "INSERT INTO settings(key, value) VALUES (@key, @value) " +
                "ON CONFLICT(key) DO UPDATE SET value = excluded.value;",
                new { key, value },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(
            new CommandDefinition(
                "DELETE FROM settings WHERE key = @key;",
                new { key },
                cancellationToken: cancellationToken)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var rows = await connection.QueryAsync<(string Key, string Value)>(
            new CommandDefinition(
                "SELECT key, value FROM settings;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);

        return rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
    }
}
