
using Amazon.DynamoDBv2.DataModel;

namespace WordList.Processing.UpdateBatch.Models;

[DynamoDBTable("prompts")]
public class Prompt
{
    [DynamoDBHashKey("batch_id")]
    public string BatchId { get; set; }

    [DynamoDBRangeKey("prompt_id")]
    public string PromptId { get; set; }

    [DynamoDBProperty("words")]
    public required List<string> Words { get; set; } = [];

    [DynamoDBProperty("text")]
    public required string Text { get; set; }

    [DynamoDBProperty("created_at")]
    public long CreatedAt { get; set; } = new DateTimeOffset(DateTime.UtcNow).ToUnixTimeSeconds();
}