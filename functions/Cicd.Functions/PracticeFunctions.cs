using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Cicd.Functions;

public sealed class PracticeFunctions(ILogger<PracticeFunctions> logger)
{
    // One invocation produces both an HTTP response and a storage queue message.
    [Function(nameof(SubmitPracticeJob))]
    public async Task<SubmitJobOutput> SubmitPracticeJob(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "practice/jobs")] HttpRequestData request)
    {
        PracticeJobRequest? input;
        try
        {
            input = await JsonSerializer.DeserializeAsync<PracticeJobRequest>(request.Body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return await BadRequest(request, "Send a JSON object with a task property.");
        }

        if (string.IsNullOrWhiteSpace(input?.Task) || input.Task.Length > 200)
        {
            return await BadRequest(request, "Task must contain between 1 and 200 characters.");
        }

        var job = new PracticeJob(Guid.NewGuid(), input.Task.Trim(), DateTimeOffset.UtcNow);
        var response = request.CreateResponse(HttpStatusCode.Accepted);
        await response.WriteAsJsonAsync(job);
        return new SubmitJobOutput { Response = response, Message = JsonSerializer.Serialize(job) };
    }

    [Function(nameof(ProcessPracticeJob))]
    public void ProcessPracticeJob(
        [QueueTrigger("practice-jobs", Connection = "AzureWebJobsStorage")] string message)
    {
        var job = JsonSerializer.Deserialize<PracticeJob>(message)
            ?? throw new InvalidOperationException("Queue message must contain a practice job.");
        if (job.Id == Guid.Empty || string.IsNullOrWhiteSpace(job.Task) || job.Task.Length > 200)
        {
            throw new InvalidOperationException("Queue message has an invalid job ID or task.");
        }

        // Practice only: real work would happen here. Queue deliveries can repeat,
        // so use the job ID to deduplicate before adding persistent side effects.
        logger.LogInformation("Processed practice job {JobId}: {Task}", job.Id, job.Task);
    }

    [Function(nameof(PracticeHeartbeat))]
    public void PracticeHeartbeat([TimerTrigger("%PracticeHeartbeatSchedule%")] TimerInfo timer)
    {
        logger.LogInformation("Practice heartbeat at {UtcNow}; past due: {IsPastDue}",
            DateTimeOffset.UtcNow, timer.IsPastDue);
    }

    private static async Task<SubmitJobOutput> BadRequest(HttpRequestData request, string error)
    {
        var response = request.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteAsJsonAsync(new { error });
        // A null queue output means invalid requests never enqueue a job.
        return new SubmitJobOutput { Response = response };
    }
}

public sealed record PracticeJobRequest(string? Task);
public sealed record PracticeJob(Guid Id, string Task, DateTimeOffset CreatedAtUtc);

public sealed class SubmitJobOutput
{
    public required HttpResponseData Response { get; init; }

    [QueueOutput("practice-jobs", Connection = "AzureWebJobsStorage")]
    public string? Message { get; init; }
}
