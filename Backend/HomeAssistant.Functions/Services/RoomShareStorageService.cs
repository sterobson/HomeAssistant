using Azure;
using Azure.Data.Tables;
using System.Security.Cryptography;

namespace HomeAssistant.Functions.Services;

/// <summary>
/// The share tokens behind each zone's or group's QR code. Table "RoomShares": PartitionKey = house ID,
/// RowKey = token, with either RoomId (a zone) or GroupId (a layout group). A token is the only thing a guest
/// holds; it never reveals the house ID. One live token per zone or group, valid until revoked.
/// </summary>
public class RoomShareStorageService
{
    private const string TableName = "RoomShares";
    private readonly TableClient _tableClient;

    public RoomShareStorageService(string connectionString)
    {
        _tableClient = new TableClient(connectionString, TableName);
    }

    /// <summary>A share for a zone (RoomId set) or a group (GroupId set); exactly one of the two.</summary>
    public record RoomShare(string HouseId, int? RoomId, string? GroupId, string Token, DateTimeOffset CreatedAt)
    {
        public bool IsFor(int? roomId, string? groupId) => groupId != null ? GroupId == groupId : RoomId == roomId && GroupId == null;
    }

    public async Task<List<RoomShare>> ListAsync(string houseId)
    {
        await _tableClient.CreateIfNotExistsAsync();

        List<RoomShare> shares = [];
        await foreach (TableEntity entity in _tableClient.QueryAsync<TableEntity>(e => e.PartitionKey == houseId))
        {
            shares.Add(ToShare(entity));
        }

        return shares;
    }

    /// <summary>The zone's or group's token, creating one if it has none.</summary>
    public async Task<RoomShare> GetOrCreateAsync(string houseId, int? roomId, string? groupId)
    {
        RoomShare? existing = (await ListAsync(houseId)).FirstOrDefault(s => s.IsFor(roomId, groupId));
        return existing ?? await CreateAsync(houseId, roomId, groupId);
    }

    /// <summary>Deletes every token for the zone or group (so old QR codes stop working) and issues a new one.</summary>
    public async Task<RoomShare> RevokeAsync(string houseId, int? roomId, string? groupId)
    {
        foreach (RoomShare share in (await ListAsync(houseId)).Where(s => s.IsFor(roomId, groupId)))
        {
            try
            {
                await _tableClient.DeleteEntityAsync(houseId, share.Token);
            }
            catch (RequestFailedException ex) when (ex.Status == 404)
            {
                // Already gone.
            }
        }

        return await CreateAsync(houseId, roomId, groupId);
    }

    /// <summary>The share a token belongs to, or null if it is unknown or revoked.</summary>
    public async Task<RoomShare?> FindByTokenAsync(string token)
    {
        if (!IsWellFormed(token))
        {
            return null;
        }

        await _tableClient.CreateIfNotExistsAsync();
        await foreach (TableEntity entity in _tableClient.QueryAsync<TableEntity>(e => e.RowKey == token))
        {
            return ToShare(entity);
        }

        return null;
    }

    private async Task<RoomShare> CreateAsync(string houseId, int? roomId, string? groupId)
    {
        await _tableClient.CreateIfNotExistsAsync();

        string token = NewToken();
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        TableEntity entity = new(houseId, token) { { "CreatedAt", createdAt } };
        if (groupId != null)
        {
            entity["GroupId"] = groupId;
        }
        else
        {
            entity["RoomId"] = roomId;
        }

        await _tableClient.AddEntityAsync(entity);
        return new RoomShare(houseId, groupId != null ? null : roomId, groupId, token, createdAt);
    }

    /// <summary>128 random bits, base64url without padding: 22 characters, safe in a URL and a QR code.</summary>
    private static string NewToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Rejects anything that couldn't be one of our tokens before it reaches a table query.</summary>
    private static bool IsWellFormed(string? token)
    {
        return token is { Length: 22 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_');
    }

    private static RoomShare ToShare(TableEntity entity)
    {
        string? groupId = entity.GetString("GroupId");
        return new RoomShare(
            entity.PartitionKey,
            groupId == null ? entity.GetInt32("RoomId") : null,
            groupId,
            entity.RowKey,
            entity.GetDateTimeOffset("CreatedAt") ?? DateTimeOffset.MinValue);
    }
}
