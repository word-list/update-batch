using Amazon.DynamoDBv2.DataModel;
using Amazon.S3;
using Amazon.SQS;
using WordList.Common.Logging;
using WordList.Common.OpenAI;
using WordList.Common.Messaging.Messages;
using WordList.Processing.UpdateBatch.Models;
using WordList.Common.Messaging;

namespace WordList.Processing.UpdateBatch;

public class BatchUpdater
{
    private static DynamoDBContext s_db = new DynamoDBContextBuilder().Build();
    private static AmazonS3Client s_s3 = new();
    private static AmazonSQSClient s_sqs = new();

    private OpenAIClient _openAI;

    protected string BatchesTableName { get; init; }
    protected string PromptsTableName { get; init; }


    protected ILogger Log { get; init; }

    public BatchUpdater(ILogger logger)
    {
        Log = logger;

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new Exception("OPENAI_API_KEY must be set");

        _openAI = new(apiKey);

        BatchesTableName = Environment.GetEnvironmentVariable("BATCHES_TABLE_NAME")
            ?? throw new Exception("BATCHES_TABLE_NAME must be set");

        PromptsTableName = Environment.GetEnvironmentVariable("PROMPTS_TABLE_NAME")
            ?? throw new Exception("PROMPTS_TABLE_NAME must be set");
    }

    private async Task<Batch?> GetBatchAsync(string id)
        => await s_db.LoadAsync<Batch>(id, new LoadConfig { OverrideTableName = BatchesTableName }).ConfigureAwait(false);

    private async Task<List<Prompt>> GetBatchPromptsAsync(string id)
        => await s_db.QueryAsync<Prompt>(id, new QueryConfig { OverrideTableName = PromptsTableName }).GetRemainingAsync().ConfigureAwait(false);

    private async Task WriteBatchAsync(Batch batch, string? status = null)
    {
        if (status is not null)
        {
            Log.Info($"Status of batch {batch.Id} has changed to: {status}");
            batch.Status = status;
        }

        await s_db.SaveAsync(batch, new SaveConfig { OverrideTableName = BatchesTableName }).ConfigureAwait(false);
    }

    private UpdateWordMessage[] GetUpdateWordMessages(string batchId, IEnumerable<(string, string?)> responses, HashSet<string> requestedWords)
    {
        var outputWords = new HashSet<string>();
        var outputMessages = new List<UpdateWordMessage>();

        var log = Log.WithPrefix($"[{batchId}]");

        foreach (var (promptId, responseText) in responses)
        {
            if (string.IsNullOrEmpty(responseText))
            {
                log.Error($"Empty response text from AI for prompt ID {promptId}");
                continue;
            }

            var responseItems = responseText.Split(",").Select(item => item.Trim().ToLower()).ToArray();
            if (responseItems.Length != 5)
            {
                log.Error($"Invalid/unexpected response text from AI for prompt ID {promptId}: {responseText}");
                continue;
            }

            /* We're expecting results in CSV format in the form:
             *  word, offensiveness, commonness, sentiment, word_types
             */
            var word = responseItems[0];
            if (outputWords.Contains(word))
            {
                log.Error($"Skipping duplicate word in response from AI for prompt ID {promptId}: {responseText}");
                continue;
            }

            if (!requestedWords.Contains(word))
            {
                log.Error($"Skipping non-requested word in response from AI for prompt ID {promptId}: {responseText}");
                continue;
            }

            int offensiveness, commonness, sentiment;
            if (!int.TryParse(responseItems[1], out offensiveness)
                || !int.TryParse(responseItems[2], out commonness)
                || !int.TryParse(responseItems[3], out sentiment))
                throw new Exception($"[{batchId}] Invalid/unexpected value in response to prompt ID {promptId}: {responseText}");

            var wordTypes = responseItems[4].Split("/").Select(item => item.Trim().ToLower()).ToArray();

            outputMessages.Add(new UpdateWordMessage
            {
                Word = word,
                Offensiveness = offensiveness,
                Commonness = commonness,
                Sentiment = sentiment,
                WordTypes = wordTypes
            });
            outputWords.Add(word);
        }

        return outputMessages.ToArray();
    }

    public async Task UpdateBatchAsync(string id)
    {
        Log.Info($"Starting to update batch {id}");

        var batch = await GetBatchAsync(id).ConfigureAwait(false)
            ?? throw new Exception($"[{id}] Failed to retrieve batch, aborting");

        if (string.IsNullOrEmpty(batch.OpenAIBatchId))
            throw new Exception($"[{id}] OpenAIBatchId is not present, aborting");

        await WriteBatchAsync(batch, "Fetching requested prompts").ConfigureAwait(false);
        var requestedWords = (await GetBatchPromptsAsync(id).ConfigureAwait(false))
            .SelectMany(prompt => prompt.Words)
            .Select(word => word.ToLower())
            .OfType<string>()
            .ToHashSet();

        await WriteBatchAsync(batch, "Retrieving batch information").ConfigureAwait(false);
        var openAIBatch = await _openAI.GetBatchStatusAsync(batch.OpenAIBatchId)
            ?? throw new Exception($"[{id}] Failed to retrieve batch information from OpenAI");

        await WriteBatchAsync(batch, "Retrieving response").ConfigureAwait(false);
        var openAIResults = await _openAI.GetCompletedBatchFileContentAsync(openAIBatch.OutputFileId).ConfigureAwait(false)
            ?? throw new Exception($"[{id}] Failed to retrieve file content from OpenAI");

        await WriteBatchAsync(batch, "Parsing results").ConfigureAwait(false);

        var items = openAIResults.Select(result => (result.CustomId, result.Response.Body.Output[0].Content[0].Text));
        var updateWordMessages = GetUpdateWordMessages(batch.Id, items, requestedWords).ToList();

        // Figure out which words we need to re-request
        var wordsToQuery = requestedWords.Except(updateWordMessages.Select(message => message.Word)).ToList();


        await WriteBatchAsync(batch, "Requesting word updates").ConfigureAwait(false);

        if (updateWordMessages.Count > 0)
        {
            Log.Info($"Sending {updateWordMessages.Count} update word message(s)");
            await MessageQueues.UpdateWords
                .SendBatchedMessagesAsync(Log, updateWordMessages)
                .ConfigureAwait(false);
        }
        else
        {
            Log.Info("No update word messages to send");
        }

        if (wordsToQuery.Count > 0)
        {
            Log.Info($"Re-requesting {wordsToQuery.Count} word(s)");
            await MessageQueues.QueryWords
                .SendBatchedMessagesAsync(
                    Log,
                    wordsToQuery.Select(word => new QueryWordMessage { Word = word })
                )
                .ConfigureAwait(false);
        }
        else
        {
            Log.Info("No words need to be re-requested");
        }

        await WriteBatchAsync(batch, "Completed").ConfigureAwait(false);
    }
}

