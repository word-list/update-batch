
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.RuntimeSupport;
using Amazon.Lambda.Serialization.SystemTextJson;
using Amazon.Lambda.SQSEvents;
using WordList.Common.Messaging;
namespace WordList.Processing.UpdateBatch;

public class Function
{
    public static async Task<string> FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        var log = new LambdaContextLogger(context);

        log.Info("Entering UpdateBatch FunctionHandler");

        if (input.Records.Count > 1)
        {
            log.Warning($"Attempting to handle {input.Records.Count} messages - batch size should be set to 1!");
        }

        var messages = MessageQueues.UpdateBatch.Receive(input, log);

        var updater = new BatchUpdater(log);

        foreach (var batchId in messages.Select(message => message.BatchId))
        {
            try
            {
                await updater.UpdateBatchAsync(batchId);
            }
            catch (Exception ex)
            {
                log.Warning($"Failed to update batch {batchId}: {ex.Message}");
            }
        }

        log.Info("Exiting UpdateBatch FunctionHandler");

        return "ok";
    }

    public static async Task Main()
    {
        Func<SQSEvent, ILambdaContext, Task<string>> handler = FunctionHandler;
        await LambdaBootstrapBuilder.Create(handler, new SourceGeneratorLambdaJsonSerializer<LambdaFunctionJsonSerializerContext>())
            .Build()
            .RunAsync();
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(SQSEvent))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
}