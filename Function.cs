
using System.Text.Json.Serialization;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using WordList.Common.Messaging;
using WordList.Common.Messaging.Messages;
namespace WordList.Processing.UpdateBatch;

public class Function
{
    public static async Task<string> FunctionHandler(SQSEvent input, ILambdaContext context)
    {
        var log = new LambdaContextLogger(context);

        log.Info("Entering UpdateBatch FunctionHandler");

        if (input.Records.Count > 1)
        {
            context.Logger.LogWarning($"Attempting to handle {input.Records.Count} messages - batch size should be set to 1!");
        }

        var messages = MessageQueues.UpdateBatch.Receive(input, log);

        var updater = new BatchUpdater(log);

        log.Info("Exiting UpdateBatch FunctionHandler");

        return "ok";
    }
}

[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(SQSEvent))]
public partial class LambdaFunctionJsonSerializerContext : JsonSerializerContext
{
}