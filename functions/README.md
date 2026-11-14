# Azure Functions practice

This separate .NET 10 isolated-worker app demonstrates three triggers without
requiring the MVC app or its SQL database to be running.

| Function | Trigger | What to observe |
| --- | --- | --- |
| SubmitPracticeJob | POST `/api/practice/jobs` | Validates JSON, returns HTTP 202 with a job ID, and writes to `practice-jobs` |
| ProcessPracticeJob | Storage queue | Logs the job ID and task when a message arrives |
| PracticeHeartbeat | Timer | Logs every five minutes on UTC five-minute boundaries |

## Run locally

Install the .NET 10 SDK, Azure Functions Core Tools v4 (Windows MSI recommended),
and Azurite. These are local development tools; no Azure subscription is needed.
Tool documentation:
[Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local)
and [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite).

From the repository root, start the storage emulator in one terminal:

```powershell
azurite --location ./tmp/.azurite
```

In another terminal:

```powershell
Copy-Item functions/Cicd.Functions/local.settings.example.json functions/Cicd.Functions/local.settings.json
Set-Location functions/Cicd.Functions
func start
```

Copy the settings only on initial setup to avoid overwriting your local changes.
Keep Azurite running: queue triggers and timer coordination require storage.
`local.settings.json` is ignored by Git and excluded from publishing.

From a third terminal, submit a job:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:7071/api/practice/jobs -ContentType application/json -Body '{"task":"Learn Azure queue triggers"}'
```

Expect a job ID in the response, then a `Processed practice job` log in the
Functions terminal. An empty task, task longer than 200 characters, or malformed
JSON returns HTTP 400 without enqueueing a message. A 202 response means the job
was queued; it does not mean processing has finished. The sample only logs work.

Change `PracticeHeartbeatSchedule` in local settings to `*/30 * * * * *` to
practice a 30-second timer, then restart the Functions host. The schedule uses six
fields, with seconds first. It does not run immediately on startup.

## Practice exercises

1. Submit several jobs and follow each ID through the HTTP response and queue log.
2. Temporarily throw an exception in `ProcessPracticeJob`, then submit a new job.
   Observe retries and eventual movement to `practice-jobs-poison` after five
   unsuccessful attempts. Remove the exception afterward. Use Azure Storage
   Explorer connected to Azurite to inspect the queues.
3. Add a blob output or persist completed jobs. Queue delivery can repeat, so
   deduplicate by job ID before adding effects such as sending email or writing
   payments. This sample has no persistent job status endpoint.

## CI and Azure

The solution includes the Functions project, so solution builds compile it.
The `FunctionsPackage` pipeline stage publishes a separate `functions` artifact
containing `functions.zip`. Existing web deployment stages still deploy the MVC app.
The pipeline does not provision or deploy a Function App.

For a later Azure deployment, create a separate Functions v4 app with the .NET 10
isolated stack and a compatible hosting plan. Set `FUNCTIONS_WORKER_RUNTIME` to
`dotnet-isolated`, `AzureWebJobsStorage` to real Azure Storage configuration, and
`PracticeHeartbeatSchedule` to `0 */5 * * * *` in the Function App settings.
Local settings are not uploaded. The HTTP trigger uses function-key authorization
in Azure; Core Tools normally disables that enforcement locally. Call the Azure
endpoint with an `x-functions-key` header and keep keys out of source control.

Reference: [Microsoft isolated worker guide](https://learn.microsoft.com/azure/azure-functions/dotnet-isolated-process-guide).
