using MySqlConnector;

namespace StockEngine.Api.Services;

// Tüm servislerin paylaştığı bağlantı/transaction yardımcısı (deadlock'ta otomatik yeniden deneme dahil).
public class Database
{
    private const int MaxRetries = 3;

    private readonly string _connectionString;
    private readonly ILogger<Database> _logger;

    public Database(IConfiguration configuration, ILogger<Database> logger)
    {
        _connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil.");
        _logger = logger;
    }

    public async Task<MySqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var conn = new MySqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(ct);
            return conn;
        }
        catch
        {
            await conn.DisposeAsync();
            throw;
        }
    }

    public async Task<T> InTransactionAsync<T>(
        Func<MySqlConnection, MySqlTransaction, Task<T>> work, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            await using var conn = await OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
            try
            {
                var result = await work(conn, tx);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (MySqlException ex) when (attempt < MaxRetries &&
                (ex.ErrorCode == MySqlErrorCode.LockDeadlock || ex.ErrorCode == MySqlErrorCode.LockWaitTimeout))
            {
                _logger.LogWarning("Transaction {Code} nedeniyle yeniden deneniyor (deneme {Attempt}).", ex.ErrorCode, attempt);
                await tx.RollbackAsync(ct);
                await Task.Delay(Random.Shared.Next(5, 25 * attempt), ct);
            }
        }
    }
}
