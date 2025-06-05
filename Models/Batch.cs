using Amazon.DynamoDBv2.DataModel;

namespace WordList.Processing.UpdateBatch.Models;

[DynamoDBTable("batches")]
public class Batch
{
    [DynamoDBHashKey("id")]
    public string Id { get; set; }

    [DynamoDBProperty("openai_batch_id")]
    public string? OpenAIBatchId { get; set; }

    [DynamoDBProperty("status")]
    public string Status { get; set; } = "Unknown";

    [DynamoDBProperty("error_message")]
    public string? ErrorMessage { get; set; }
}

