using System.Net;
using System.Diagnostics;
using System.Text;
using System.Threading;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Agent;
using STS2AIAgent.Game;

namespace STS2AIAgent.Server;

internal static class Router
{
    private const string ServiceName = "creative-ai";
    private const string ProtocolVersion = "2026-03-11-v1";
    private const string ModVersion = "0.15.1";
    private const string LogPrefix = "[STS2AIAgent.Router]";

    private static long _requestCounter;

    public static async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var seq = Interlocked.Increment(ref _requestCounter);
        var requestId = $"req_{DateTime.UtcNow:yyyyMMdd_HHmmss_ffff}_{seq}";
        var request = context.Request;
        var response = context.Response;
        var stopwatch = Stopwatch.StartNew();
        var statusCode = 500;

        try
        {
            Log.Info($"{LogPrefix} {requestId} {request.HttpMethod} {request.Url?.AbsolutePath}");

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/health")
            {
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = new
                    {
                        service = ServiceName,
                        mod_version = ModVersion,
                        protocol_version = ProtocolVersion,
                        game_version = ReleaseInfoManager.Instance.ReleaseInfo?.Version ?? "unknown",
                        status = "ready"
                    }
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/state")
            {
                var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = state
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/actions/available")
            {
                var payload = await GameThread.InvokeAsync(GameStateService.BuildAvailableActionsPayload);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = payload
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/action")
            {
                var actionRequest = await JsonHelper.DeserializeAsync<ActionRequest>(request.InputStream, cancellationToken);
                if (actionRequest?.action == null)
                {
                    throw new ApiException(400, "invalid_request", "Request body must contain an action field.");
                }

                var actionResponse = await GameThread.InvokeAsync(() => GameActionService.ExecuteAsync(actionRequest));
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = actionResponse
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/snapshot")
            {
                var snapshot = await AiAgentService.Instance.GetSnapshotAsync(refreshObservedState: true);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = snapshot
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/config")
            {
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = AiAgentService.Instance.GetSnapshot().config
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/config")
            {
                var config = await JsonHelper.DeserializeAsync<AiAgentConfig>(request.InputStream, cancellationToken);
                if (config == null)
                {
                    throw new ApiException(400, "invalid_request", "Request body must contain a valid AI config object.");
                }

                AiAgentService.Instance.SaveConfig(config);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = AiAgentService.Instance.GetSnapshot().config
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/request-step")
            {
                AiAgentService.Instance.RequestSingleStep();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/start")
            {
                var startResult = await AiAgentService.Instance.ResumeAutomationAsync();
                if (!startResult.ok)
                {
                    throw new ApiException(409, "invalid_state", startResult.reason);
                }

                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/pause")
            {
                AiAgentService.Instance.PauseAutomation();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/execute-pending")
            {
                AiAgentService.Instance.ExecutePendingDecision();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/stop")
            {
                AiAgentService.Instance.Stop();
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId
                });
                statusCode = 200;
                return;
            }

            if (request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase) &&
                request.Url?.AbsolutePath == "/agent/test-llm")
            {
                var config = await JsonHelper.DeserializeAsync<AiAgentConfig>(request.InputStream, cancellationToken);
                var result = await AiAgentService.Instance.TestConnectionAsync(config, cancellationToken);
                await WriteJsonAsync(response, 200, new
                {
                    ok = true,
                    request_id = requestId,
                    data = result
                });
                statusCode = 200;
                return;
            }

            statusCode = 404;
            await WriteErrorAsync(response, statusCode, "not_found", "Route not found.", requestId);
        }
        catch (ApiException ex)
        {
            statusCode = ex.StatusCode;
            await WriteErrorAsync(response, ex.StatusCode, ex.Code, ex.Message, requestId, ex.Details, ex.Retryable);
        }
        catch (Exception ex)
        {
            Log.Error($"{LogPrefix} {requestId} Failed: {ex}");
            statusCode = 500;
            await WriteErrorAsync(response, statusCode, "internal_error", "Unhandled server error.", requestId);
        }
        finally
        {
            Log.Info($"{LogPrefix} {requestId} Completed {statusCode} in {stopwatch.ElapsedMilliseconds}ms");
            response.Close();
        }
    }

    public static Task WriteErrorAsync(
        HttpListenerResponse response,
        int statusCode,
        string code,
        string message,
        string? requestId = null,
        object? details = null,
        bool retryable = false)
    {
        return WriteJsonAsync(response, statusCode, new
        {
            ok = false,
            request_id = requestId ?? $"req_{DateTime.UtcNow:yyyyMMdd_HHmmss_ffff}_{Interlocked.Increment(ref _requestCounter)}",
            error = new
            {
                code,
                message,
                details,
                retryable
            }
        });
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload)
    {
        var json = JsonHelper.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);

        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.LongLength;

        await response.OutputStream.WriteAsync(bytes);
    }
}
