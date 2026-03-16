using MegaCrit.Sts2.Core.Logging;
using STS2AIAgent.Game;

namespace STS2AIAgent.Agent;

internal sealed class AiAgentService
{
    private const string LogPrefix = "[STS2AIAgent.AI]";
    private const int MaxLogEntries = 200;
    private const int MaxRuntimeNotes = 6;
    private static readonly TimeSpan AutoLoopProbeInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CombatFallbackResyncDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan CombatPlayerTurnResumeDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CombatContinuationProbeInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan CombatContinuationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RouteContinuationProbeInterval = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan RouteContinuationTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ConfigRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private readonly AiConfigStore _configStore = new();
    private readonly AiKnowledgeStore _knowledgeStore = new();
    private readonly AiPromptBuilder _promptBuilder = new();
    private readonly AiOpenAiCompatibleClient _llmClient = new();
    private readonly AiActionValidator _validator = new();
    private readonly Dictionary<AiRuntimeKind, AgentRuntimeContext> _contexts = new()
    {
        [AiRuntimeKind.Combat] = new(AiRuntimeKind.Combat),
        [AiRuntimeKind.Route] = new(AiRuntimeKind.Route)
    };

    private AiAgentConfig _config = new();
    private readonly List<AiLogEntry> _logs = new();

    private CancellationTokenSource? _activeCts;
    private bool _isBusy;
    private DateTime? _configLastWriteUtc;
    private DateTime _nextAutoLoopProbeUtc = DateTime.MinValue;
    private DateTime _nextConfigRefreshUtc = DateTime.MinValue;
    private AiRuntimeKind _activeRuntime = AiRuntimeKind.Route;
    private string _status = "空闲";
    private string _stateSummary = "尚未采集游戏状态。";
    private bool _automationPaused;
    private int _combatContinuationGeneration;
    private int _routeContinuationGeneration;
    private string _currentCharacterId = string.Empty;
    private string _currentCharacterName = string.Empty;
    private string _currentScreen = "UNKNOWN";
    private string _sessionPhase = "menu";
    private bool _canStartAutomation;
    private string _startBlockReason = "请先进入一局爬塔后再启动托管。";
    private bool _lastRunnableState;

    public static AiAgentService Instance { get; } = new();

    private AiAgentService()
    {
    }

    public void Initialize()
    {
        var loadedConfig = _configStore.Load().Sanitize();
        if (loadedConfig.enable_agent)
        {
            loadedConfig = new AiAgentConfig
            {
                enable_agent = false,
                provider = loadedConfig.provider,
                base_url = loadedConfig.base_url,
                model = loadedConfig.model,
                api_key = loadedConfig.api_key,
                temperature = loadedConfig.temperature,
                auto_execute = loadedConfig.auto_execute,
                auto_combat_loop = loadedConfig.auto_combat_loop,
                character_combat_prompts = new Dictionary<string, string>(loadedConfig.character_combat_prompts, StringComparer.OrdinalIgnoreCase),
                character_route_prompts = new Dictionary<string, string>(loadedConfig.character_route_prompts, StringComparer.OrdinalIgnoreCase)
            };
        }

        _configStore.Save(loadedConfig);

        lock (_gate)
        {
            _config = loadedConfig;
            _configLastWriteUtc = _configStore.GetLastWriteTimeUtc();
            _status = loadedConfig.enable_agent ? "就绪" : "未启动";
            foreach (var context in _contexts.Values)
            {
                context.Status = _status;
            }
        }

        AddLog("INFO", $"Loaded config provider={loadedConfig.provider}, model={loadedConfig.model}, key={AiSecretMasker.Mask(loadedConfig.api_key)}, enable_agent={loadedConfig.enable_agent}, auto_execute={loadedConfig.auto_execute}, auto_combat_loop={loadedConfig.auto_combat_loop}");
        AddLog("INFO", "Agent startup policy: enable_agent is forced to false on game launch until you click start.");
        AddLog("INFO", $"Detailed LLM config path: {AiRuntimePaths.ConfigPath}");
        AddLog("INFO", $"Knowledge root: {AiRuntimePaths.KnowledgeRoot}");
    }

    public void Shutdown()
    {
        Stop(clearPendingDecision: false);

        lock (_gate)
        {
            _configStore.Save(_config);
        }

    }

    public AiRuntimeSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var activeContext = _contexts[_activeRuntime];
            return new AiRuntimeSnapshot
            {
                is_busy = _isBusy,
                agent_enabled = _config.enable_agent,
                automation_paused = _automationPaused,
                auto_combat_loop = _config.auto_combat_loop,
                active_runtime = FormatRuntimeKind(_activeRuntime),
                status = _status,
                state_summary = _stateSummary,
                plan_summary = activeContext.PlanSummary,
                reasoning = activeContext.Reasoning,
                pending_action = activeContext.PendingAction,
                has_pending_action = activeContext.PendingDecision?.action.HasAction == true,
                last_action_result = activeContext.LastActionResult,
                error = activeContext.Error,
                current_character_id = _currentCharacterId,
                current_character_name = _currentCharacterName,
                current_screen = _currentScreen,
                session_phase = _sessionPhase,
                can_start_automation = _canStartAutomation,
                start_block_reason = _startBlockReason,
                config = CreateDisplayConfig(_config),
                runtime_contexts = _contexts.Values
                    .OrderBy(context => context.Kind)
                    .Select(CreateRuntimeSnapshot)
                    .ToArray(),
                logs = _logs.ToArray()
            };
        }
    }

    public void SaveConfig(AiAgentConfig config)
    {
        var sanitized = config.Sanitize();
        ApplyConfig(sanitized, "API", persist: true);
    }

    public void RequestSingleStep(bool forceCombatState = false)
    {
        CancellationToken token;

        lock (_gate)
        {
            if (!_config.enable_agent)
            {
                AddLog("WARN", "AI agent is disabled in config. Set enable_agent=true to run a step.");
                return;
            }

            if (_isBusy)
            {
                AddLog("WARN", "A request is already running.");
                return;
            }

            _activeCts = new CancellationTokenSource();
            token = _activeCts.Token;
            _isBusy = true;
            _status = forceCombatState ? "正在强制刷新战斗状态" : "正在读取游戏状态";
        }

        AddLog("INFO", $"Starting single-step request in {(_config.auto_execute ? "auto-execute" : "advisory")} mode{(forceCombatState ? " with forced combat refresh" : string.Empty)}.");
        _ = RunSingleStepAsync(token, forceCombatState);
    }

    public async Task<(bool ok, string reason)> ResumeAutomationAsync()
    {
        var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);
        CaptureObservedState(state);
        var canStartImmediately = TryEvaluateStartAvailability(state, out var reason);
        if (!canStartImmediately)
        {
            AddLog("WARN", $"Rejected automation start: {reason}");
            return (false, reason);
        }

        AiAgentConfig? configToPersist = null;
        var requestImmediateStep = false;

        lock (_gate)
        {
            if (!_config.enable_agent)
            {
                configToPersist = new AiAgentConfig
                {
                    enable_agent = true,
                    provider = _config.provider,
                    base_url = _config.base_url,
                    model = _config.model,
                    api_key = _config.api_key,
                    temperature = _config.temperature,
                    auto_execute = _config.auto_execute,
                    auto_combat_loop = _config.auto_combat_loop,
                    character_combat_prompts = new Dictionary<string, string>(_config.character_combat_prompts, StringComparer.OrdinalIgnoreCase),
                    character_route_prompts = new Dictionary<string, string>(_config.character_route_prompts, StringComparer.OrdinalIgnoreCase)
                };
                _config = configToPersist;
                _configLastWriteUtc = null;
            }

            _automationPaused = false;
            _status = _isBusy ? _status : "就绪";
            foreach (var context in _contexts.Values)
            {
                if (context.Status is "已暂停" or "未启动" or "已停止" or "等待进入游戏")
                {
                    context.Status = "就绪";
                }

                context.Error = string.Empty;
            }

            requestImmediateStep = !_isBusy && _config.auto_combat_loop;
        }

        if (configToPersist != null)
        {
            _configStore.Save(configToPersist);
            lock (_gate)
            {
                _configLastWriteUtc = _configStore.GetLastWriteTimeUtc();
            }
        }

        AddLog("INFO", configToPersist != null
            ? "Agent automation resumed and enable_agent was turned on from the overlay."
            : "Agent automation resumed from the overlay.");

        if (requestImmediateStep)
        {
            _ = ProbeAutoLoopAsync();
        }

        return (true, string.Empty);
    }

    public void ExecutePendingDecision()
    {
        CancellationToken token;
        AiDecisionResult decision;
        AiRuntimeKind runtimeKind;

        lock (_gate)
        {
            if (!_config.enable_agent)
            {
                AddLog("WARN", "AI agent is disabled in config. Refusing to execute pending action.");
                return;
            }

            if (_isBusy)
            {
                AddLog("WARN", "Cannot execute while another request is running.");
                return;
            }

            if (!TryGetPendingDecisionLocked(out runtimeKind, out decision))
            {
                AddLog("WARN", "No pending action is available to execute.");
                return;
            }

            _activeRuntime = runtimeKind;
            _activeCts = new CancellationTokenSource();
            token = _activeCts.Token;
            _isBusy = true;
            _status = $"正在校验{FormatRuntimeKindZh(runtimeKind)}动作";
            _contexts[runtimeKind].Status = _status;
            _contexts[runtimeKind].Error = string.Empty;
        }

        AddLog("INFO", $"Executing {FormatRuntimeKind(runtimeKind)} suggestion: {AiActionValidator.FormatAction(decision.action)}");
        _ = ExecuteDecisionAsync(runtimeKind, decision, token, autoTriggered: false);
    }

    public void Stop(bool clearPendingDecision = true)
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            cts = _activeCts;
            _activeCts = null;
            _isBusy = false;
            _combatContinuationGeneration++;
            _routeContinuationGeneration++;
            _status = _config.enable_agent ? "已停止" : "未启动";
            foreach (var context in _contexts.Values)
            {
                context.Status = _status;
                context.Error = string.Empty;
                if (clearPendingDecision)
                {
                    context.PendingDecision = null;
                    context.PendingFingerprint = null;
                    context.PendingAction = string.Empty;
                }
            }
        }

        cts?.Cancel();
        AddLog("INFO", "Agent stop requested.");
    }

    public void PauseAutomation()
    {
        Stop(clearPendingDecision: false);

        lock (_gate)
        {
            _automationPaused = true;
            _status = "已暂停";
            foreach (var context in _contexts.Values)
            {
                context.Status = "已暂停";
                context.Error = string.Empty;
            }
        }

        AddLog("INFO", "Agent automation paused from the overlay.");
    }

    public async Task<AiConnectivityProbeResult> TestConnectionAsync(AiAgentConfig? overrideConfig, CancellationToken cancellationToken)
    {
        AiAgentConfig config;
        lock (_gate)
        {
            config = (overrideConfig ?? _config).Sanitize();
        }

        var startedUtc = DateTime.UtcNow;
        var reply = await _llmClient.TestConnectionAsync(config, cancellationToken).ConfigureAwait(false);
        return new AiConnectivityProbeResult
        {
            ok = true,
            tested_at_utc = startedUtc,
            provider = config.provider,
            base_url = config.base_url,
            model = config.model,
            reply = reply
        };
    }

    public void Tick()
    {
        RefreshObservedState();
        ReloadConfigIfChanged();
        MaybeQueueAutoLoop();
    }

    private void RefreshObservedState()
    {
        try
        {
            CaptureObservedState(GameStateService.BuildStatePayload());
        }
        catch (Exception ex)
        {
            AddLog("WARN", $"Observed state refresh failed: {ex.Message}");
        }
    }

    private void CaptureObservedState(GameStatePayload state)
    {
        var (characterId, characterName) = ResolveCurrentCharacter(state);
        var canStart = TryEvaluateStartAvailability(state, out var blockReason);
        var shouldWakeFromPendingStart = false;

        lock (_gate)
        {
            shouldWakeFromPendingStart =
                !_lastRunnableState &&
                canStart &&
                _config.enable_agent &&
                !_automationPaused &&
                !_isBusy &&
                !_contexts.Values.Any(context => context.PendingDecision != null);

            _lastRunnableState = canStart;
            _currentCharacterId = characterId;
            _currentCharacterName = characterName;
            _currentScreen = state.screen;
            _sessionPhase = state.session.phase;
            _canStartAutomation = canStart;
            _startBlockReason = blockReason;
        }

        if (shouldWakeFromPendingStart)
        {
            AddLog("INFO", $"Detected playable in-run state on screen={state.screen}; waking auto-loop.");
            _ = ProbeAutoLoopAsync();
        }
    }

    private static bool TryEvaluateStartAvailability(GameStatePayload state, out string reason)
    {
        var inRunPhase = string.Equals(state.session.phase, "run", StringComparison.OrdinalIgnoreCase);
        var inRunnableScreen = IsRunnableScreen(state.screen);

        if (!inRunPhase && !inRunnableScreen)
        {
            reason = "请先进入一局爬塔后再启动托管。";
            return false;
        }

        if (state.game_over != null)
        {
            reason = "当前本局已结束，请开始新局后再启动托管。";
            return false;
        }

        if (state.run == null)
        {
            reason = "尚未进入可托管的对局状态，请进入地图、战斗或结算节点后再启动。";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsRunnableScreen(string? screen)
    {
        return screen switch
        {
            "COMBAT" => true,
            "MAP" => true,
            "EVENT" => true,
            "REST" => true,
            "SHOP" => true,
            "CHEST" => true,
            "REWARD" => true,
            "CARD_SELECTION" => true,
            _ => false
        };
    }

    private static (string characterId, string characterName) ResolveCurrentCharacter(GameStatePayload state)
    {
        if (state.run != null)
        {
            return (state.run.character_id ?? string.Empty, state.run.character_name ?? string.Empty);
        }

        if (state.multiplayer_lobby?.selected_character_id is { Length: > 0 } selectedCharacterId)
        {
            return (selectedCharacterId, string.Empty);
        }

        return (string.Empty, string.Empty);
    }

    private async Task RunSingleStepAsync(CancellationToken cancellationToken, bool forceCombatState = false)
    {
        try
        {
            var config = _config.Clone();
            var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);
            CaptureObservedState(state);
            var runtimeKind = DetermineRuntimeKind(state);
            if (runtimeKind == AiRuntimeKind.Combat && !state.combat_actions_ready && !forceCombatState)
            {
                lock (_gate)
                {
                    _isBusy = false;
                    _activeCts = null;
                    ApplyCombatWaitingStatusLocked(state);
                }

                AddLog("INFO", $"Combat state is not actionable yet; waiting for transition state={state.combat_turn_state}.");
                return;
            }

            if (runtimeKind == AiRuntimeKind.Combat && forceCombatState && !state.combat_actions_ready)
            {
                AddLog("WARN", $"Forcing combat resync while state still reports {state.combat_turn_state}; validator will decide whether execution is currently possible.");
            }

            AgentRuntimeContext runtimeContext;

            lock (_gate)
            {
                _activeRuntime = runtimeKind;
                runtimeContext = _contexts[runtimeKind];
                runtimeContext.Status = $"正在构建{FormatRuntimeKindZh(runtimeKind)}提示词";
                runtimeContext.Error = string.Empty;
                runtimeContext.PendingDecision = null;
                runtimeContext.PendingFingerprint = null;
                runtimeContext.PendingAction = string.Empty;
                SetInactiveContextStandby(runtimeKind);
            }

            UpdateStateSummary(state, runtimeKind, runtimeContext.Status);

            var knowledgeSnippet = _knowledgeStore.BuildPromptSupplement(state);
            var prompt = _promptBuilder.Build(
                state,
                config,
                knowledgeSnippet,
                new AiRuntimePromptContext
                {
                    runtime_kind = runtimeKind,
                    recent_notes = runtimeContext.RecentNotes.ToArray()
                });
            AddLog("INFO", $"Requesting {FormatRuntimeKind(runtimeKind)} LLM suggestion for screen={state.screen}, turn={(state.turn?.ToString() ?? "null")}.");

            var decision = await _llmClient.RequestDecisionAsync(config, prompt, cancellationToken).ConfigureAwait(false);
            var originalActionName = decision.action.name;
            decision = MaybeApplyCombatTurnFallback(state, runtimeKind, decision);
            if (!string.Equals(originalActionName, decision.action.name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(decision.action.name, "end_turn", StringComparison.OrdinalIgnoreCase))
            {
                AddLog("INFO", "Applied combat fallback: no playable cards or usable potions remained, so the agent will end the turn.");
            }
            _knowledgeStore.AppendDecision(state, decision);
            var pendingActionText = DescribePlannedAction(state, decision.action);

            lock (_gate)
            {
                runtimeContext.PendingDecision = decision;
                runtimeContext.PendingFingerprint = GameStateFingerprint.FromState(state);
                runtimeContext.PlanSummary = decision.plan_summary;
                runtimeContext.Reasoning = decision.reasoning;
                runtimeContext.PendingAction = pendingActionText;
                runtimeContext.Status = decision.action.HasAction ? "建议已生成" : "正在观察";
                runtimeContext.Error = string.Empty;
                AddRuntimeNote(runtimeContext, $"plan={SanitizeForNote(decision.plan_summary)} | action={SanitizeForNote(runtimeContext.PendingAction)}");
                _status = runtimeContext.Status;
            }

            AddLog("INFO", $"{FormatRuntimeKind(runtimeKind)} suggestion ready: {pendingActionText}");
            if (!string.IsNullOrWhiteSpace(decision.stop_reason))
            {
                AddLog("INFO", $"{FormatRuntimeKind(runtimeKind)} stop reason: {decision.stop_reason}");
            }

            if (runtimeKind == AiRuntimeKind.Combat &&
                config.auto_execute &&
                !decision.action.HasAction &&
                !state.combat_actions_ready)
            {
                lock (_gate)
                {
                    _isBusy = false;
                    _activeCts = null;
                    ApplyCombatWaitingStatusLocked(state);
                }

                AddLog("INFO", $"Combat suggestion returned no action during non-actionable state={state.combat_turn_state}; resuming combat continuation monitoring.");
                BeginCombatContinuationMonitoring(state, decision.action.name, state.turn);
                return;
            }

            if (config.auto_execute && decision.action.HasAction)
            {
                AddLog("INFO", $"Auto-execute is enabled. Executing the {FormatRuntimeKind(runtimeKind)} action immediately.");
                await ExecuteDecisionAsync(runtimeKind, decision, cancellationToken, autoTriggered: true).ConfigureAwait(false);
                return;
            }

            lock (_gate)
            {
                _isBusy = false;
                _activeCts = null;
                runtimeContext.Status = decision.requires_confirmation && decision.action.HasAction
                    ? "等待确认"
                    : runtimeContext.Status;
                _status = runtimeContext.Status;
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _isBusy = false;
                _activeCts = null;
                _status = _config.enable_agent ? "已取消" : "未启动";
                _contexts[_activeRuntime].Status = _status;
            }

            AddLog("WARN", "Agent request cancelled.");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                var runtimeContext = _contexts[_activeRuntime];
                _isBusy = false;
                _activeCts = null;
                _status = "出错";
                runtimeContext.Status = "出错";
                runtimeContext.Error = ex.Message;
                AddRuntimeNote(runtimeContext, $"error={SanitizeForNote(ex.Message)}");
            }

            AddLog("ERROR", $"Single-step request failed: {ex.Message}");
        }
    }

    private async Task ExecuteDecisionAsync(AiRuntimeKind runtimeKind, AiDecisionResult decision, CancellationToken cancellationToken, bool autoTriggered)
    {
        try
        {
            var currentState = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);
            CaptureObservedState(currentState);
            UpdateStateSummary(currentState, runtimeKind, $"正在校验{FormatRuntimeKindZh(runtimeKind)}动作");
            var actionSummary = DescribeExecutedAction(currentState, decision.action);

            lock (_gate)
            {
                var runtimeContext = _contexts[runtimeKind];
                if (runtimeContext.PendingFingerprint != null && !runtimeContext.PendingFingerprint.Matches(currentState))
                {
                    throw new InvalidOperationException("State changed since the suggestion was generated. Request a fresh step.");
                }
            }

            if (!_validator.TryBuildValidatedRequest(currentState, decision, out var request, out var validationError) || request == null)
            {
                throw new InvalidOperationException(validationError);
            }

            var response = await GameThread.InvokeAsync(() => GameActionService.ExecuteAsync(request)).ConfigureAwait(false);
            CaptureObservedState(response.state);
            _knowledgeStore.AppendExecutionResult(response.state, decision, response.message);
            UpdateStateSummary(response.state, runtimeKind, $"{FormatRuntimeKindZh(runtimeKind)}动作已完成");

            lock (_gate)
            {
                var runtimeContext = _contexts[runtimeKind];
                runtimeContext.PendingDecision = null;
                runtimeContext.PendingFingerprint = null;
                runtimeContext.PendingAction = string.Empty;
                runtimeContext.LastActionResult = actionSummary;
                runtimeContext.Status = autoTriggered ? "自动执行完成" : "执行完成";
                runtimeContext.Error = string.Empty;
                AddRuntimeNote(runtimeContext, $"executed={SanitizeForNote(actionSummary)} | result={SanitizeForNote(response.message)}");
                _status = runtimeContext.Status;
                _isBusy = false;
                _activeCts = null;
            }

            AddLog("INFO", $"{FormatRuntimeKind(runtimeKind)} action completed: {actionSummary} | {response.status} | {response.message}");

            if (autoTriggered && runtimeKind == AiRuntimeKind.Combat)
            {
                BeginCombatContinuationMonitoring(response.state, decision.action.name, currentState.turn);
            }
            else if (autoTriggered)
            {
                _ = ProbeAutoLoopAsync();
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                var runtimeContext = _contexts[runtimeKind];
                _isBusy = false;
                _activeCts = null;
                _status = _config.enable_agent ? "已取消" : "未启动";
                runtimeContext.Status = _status;
            }

            AddLog("WARN", "Action execution cancelled.");
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                var runtimeContext = _contexts[runtimeKind];
                _isBusy = false;
                _activeCts = null;
                _status = "执行出错";
                runtimeContext.Status = "执行出错";
                runtimeContext.Error = ex.Message;
                AddRuntimeNote(runtimeContext, $"error={SanitizeForNote(ex.Message)}");
            }

            AddLog("ERROR", $"Action execution failed: {ex.Message}");
        }
    }

    private void UpdateStateSummary(GameStatePayload state, AiRuntimeKind runtimeKind, string status)
    {
        var summary = $"界面={TranslateScreen(state.screen)} | 当前 Agent={FormatRuntimeKindZh(runtimeKind)} | 层数={state.run?.floor} | 回合={state.turn?.ToString() ?? "-"} | 可用动作={state.available_actions.Length}";
        if (runtimeKind == AiRuntimeKind.Combat && !state.combat_actions_ready)
        {
            summary += $" | 战斗阶段={TranslateCombatTurnState(state.combat_turn_state)}";
        }

        lock (_gate)
        {
            _activeRuntime = runtimeKind;
            _stateSummary = summary;
            _status = status;
            _contexts[runtimeKind].Status = status;
            SetInactiveContextStandby(runtimeKind);
        }
    }

    private void ReloadConfigIfChanged()
    {
        if (DateTime.UtcNow < _nextConfigRefreshUtc)
        {
            return;
        }

        _nextConfigRefreshUtc = DateTime.UtcNow + ConfigRefreshInterval;

        var lastWriteUtc = _configStore.GetLastWriteTimeUtc();
        if (lastWriteUtc == null)
        {
            return;
        }

        lock (_gate)
        {
            if (_configLastWriteUtc == lastWriteUtc)
            {
                return;
            }
        }

        if (!_configStore.TryLoad(out var loadedConfig, out var loadError))
        {
            AddLog("WARN", $"Skipped config reload because the file could not be parsed: {loadError}");
            lock (_gate)
            {
                _configLastWriteUtc = lastWriteUtc;
            }

            return;
        }

        ApplyConfig(loadedConfig.Sanitize(), "disk", persist: false);
    }

    private void ApplyConfig(AiAgentConfig newConfig, string source, bool persist)
    {
        var stopRequested = false;

        lock (_gate)
        {
            if (_config.IsEquivalentTo(newConfig))
            {
                if (persist)
                {
                    _configStore.Save(newConfig);
                }

                _configLastWriteUtc = _configStore.GetLastWriteTimeUtc();
                return;
            }

            _config = newConfig;
            if (!newConfig.enable_agent)
            {
                stopRequested = _isBusy || _contexts.Values.Any(context => context.PendingDecision != null);
                _status = "未启动";
                _automationPaused = false;
                foreach (var context in _contexts.Values)
                {
                    context.Status = "未启动";
                }
            }
            else if (_status == "未启动")
            {
                _status = "就绪";
                foreach (var context in _contexts.Values)
                {
                    if (context.Status == "未启动")
                    {
                        context.Status = "就绪";
                    }
                }
            }
        }

        if (persist)
        {
            _configStore.Save(newConfig);
        }

        lock (_gate)
        {
            _configLastWriteUtc = _configStore.GetLastWriteTimeUtc();
        }

        if (stopRequested)
        {
            Stop();
        }

        AddLog("INFO", $"Reloaded config from {source}: provider={newConfig.provider}, model={newConfig.model}, key={AiSecretMasker.Mask(newConfig.api_key)}, enable_agent={newConfig.enable_agent}, auto_execute={newConfig.auto_execute}, auto_combat_loop={newConfig.auto_combat_loop}");
    }

    private void MaybeQueueAutoLoop()
    {
        lock (_gate)
        {
            if (!_config.enable_agent ||
                _automationPaused ||
                !_config.auto_combat_loop ||
                _isBusy ||
                DateTime.UtcNow < _nextAutoLoopProbeUtc)
            {
                return;
            }

            _nextAutoLoopProbeUtc = DateTime.UtcNow + AutoLoopProbeInterval;
        }

        _ = ProbeAutoLoopAsync();
    }

    private void BeginCombatContinuationMonitoring(GameStatePayload state, string? actionName, int? sourceTurn)
    {
        int generation;
        lock (_gate)
        {
            _combatContinuationGeneration++;
            generation = _combatContinuationGeneration;
        }

        if (state.in_combat && !state.combat_actions_ready)
        {
            lock (_gate)
            {
                ApplyCombatWaitingStatusLocked(state);
            }

            AddLog("INFO", $"Combat continuation monitor armed from state={state.combat_turn_state}.");
        }
        else
        {
            AddLog("INFO", $"Combat continuation monitor armed from state={state.combat_turn_state}, actions_ready={state.combat_actions_ready}.");
        }

        _ = MonitorCombatContinuationAsync(generation, actionName, sourceTurn);
    }

    private void BeginRouteContinuationMonitoring(string reason)
    {
        int generation;
        lock (_gate)
        {
            _routeContinuationGeneration++;
            generation = _routeContinuationGeneration;
        }

        AddLog("INFO", $"Route continuation monitor armed: {reason}.");
        _ = MonitorRouteContinuationAsync(generation);
    }

    private async Task ProbeAutoLoopAsync()
    {
        try
        {
            var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);
            if (!TryGetAutoLoopRuntime(state, out var runtimeKind))
            {
                if (state.in_combat && !state.combat_actions_ready)
                {
                    lock (_gate)
                    {
                        ApplyCombatWaitingStatusLocked(state);
                    }
                }
                else if (!state.in_combat &&
                    string.Equals(state.session.phase, "run", StringComparison.OrdinalIgnoreCase))
                {
                    BeginRouteContinuationMonitoring($"screen={state.screen}, available_actions={state.available_actions.Length}");
                }

                return;
            }

            lock (_gate)
            {
                var context = _contexts[runtimeKind];
                if (context.PendingDecision != null)
                {
                    return;
                }
            }

            RequestSingleStep();
        }
        catch (Exception ex)
        {
            AddLog("WARN", $"Auto-loop probe failed: {ex.Message}");
        }
    }

    private async Task MonitorCombatContinuationAsync(int generation, string? actionName, int? sourceTurn)
    {
        var deadlineUtc = DateTime.UtcNow + CombatContinuationTimeout;
        var fallbackDueUtc = DateTime.UtcNow + CombatFallbackResyncDelay;
        string? lastObservedState = null;
        var forcedResyncRequested = false;
        var shouldForceResync = string.Equals(actionName, "end_turn", StringComparison.OrdinalIgnoreCase);

        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);

                lock (_gate)
                {
                    if (generation != _combatContinuationGeneration ||
                        !_config.enable_agent ||
                        _automationPaused)
                    {
                        return;
                    }

                    if (_isBusy)
                    {
                        return;
                    }
                }

                if (!state.in_combat)
                {
                    AddLog("INFO", "Combat continuation monitor detected combat end; switching to route continuation monitoring.");
                    BeginRouteContinuationMonitoring("combat ended");
                    return;
                }

                if (state.combat_actions_ready)
                {
                    AddLog("INFO", $"Combat continuation monitor detected player_actionable on turn={(state.turn?.ToString() ?? "null")}; waiting {CombatPlayerTurnResumeDelay.TotalSeconds:0}s before waking the combat agent.");
                    await Task.Delay(CombatPlayerTurnResumeDelay).ConfigureAwait(false);

                    var confirmedState = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);
                    lock (_gate)
                    {
                        if (generation != _combatContinuationGeneration ||
                            !_config.enable_agent ||
                            _automationPaused ||
                            _isBusy)
                        {
                            return;
                        }
                    }

                    if (confirmedState.in_combat && confirmedState.combat_actions_ready)
                    {
                        AddLog("INFO", $"Combat continuation monitor resumed on confirmed state={confirmedState.combat_turn_state}, turn={(confirmedState.turn?.ToString() ?? "null")}.");
                        _ = ProbeAutoLoopAsync();
                        return;
                    }

                    lock (_gate)
                    {
                        if (confirmedState.in_combat && !confirmedState.combat_actions_ready)
                        {
                            ApplyCombatWaitingStatusLocked(confirmedState);
                        }
                    }

                    AddLog("INFO", $"Combat continuation monitor delay check found state={confirmedState.combat_turn_state}; continuing to wait.");
                    lastObservedState = confirmedState.combat_turn_state;
                    continue;
                }

                if (shouldForceResync &&
                    !forcedResyncRequested &&
                    DateTime.UtcNow >= fallbackDueUtc &&
                    state.turn != sourceTurn &&
                    string.Equals(state.combat_turn_state, "player_actionable", StringComparison.OrdinalIgnoreCase) &&
                    state.combat_actions_ready)
                {
                    forcedResyncRequested = true;
                    AddLog("WARN", $"Combat continuation monitor is forcing a fresh LLM sync after {CombatFallbackResyncDelay.TotalSeconds:0}s; observed turn={(state.turn?.ToString() ?? "null")}, state={state.combat_turn_state}, available_actions={state.available_actions.Length}.");
                    RequestSingleStep(forceCombatState: true);
                    return;
                }

                lock (_gate)
                {
                    ApplyCombatWaitingStatusLocked(state);
                }

                if (!string.Equals(lastObservedState, state.combat_turn_state, StringComparison.Ordinal))
                {
                    AddLog("INFO", $"Combat continuation monitor waiting on state={state.combat_turn_state}.");
                    lastObservedState = state.combat_turn_state;
                }
            }
            catch (Exception ex)
            {
                AddLog("WARN", $"Combat continuation monitor probe failed: {ex.Message}");
            }

            await Task.Delay(CombatContinuationProbeInterval).ConfigureAwait(false);
        }

        AddLog("WARN", "Combat continuation monitor timed out while waiting for the next actionable player state.");
    }

    private async Task MonitorRouteContinuationAsync(int generation)
    {
        var deadlineUtc = DateTime.UtcNow + RouteContinuationTimeout;
        string? lastObservedScreen = null;

        while (DateTime.UtcNow < deadlineUtc)
        {
            try
            {
                var state = await GameThread.InvokeAsync(GameStateService.BuildStatePayload).ConfigureAwait(false);

                lock (_gate)
                {
                    if (generation != _routeContinuationGeneration ||
                        !_config.enable_agent ||
                        _automationPaused)
                    {
                        return;
                    }

                    if (_isBusy)
                    {
                        return;
                    }
                }

                if (state.in_combat)
                {
                    AddLog("INFO", "Route continuation monitor detected a return to combat; handing control back to the main auto-loop.");
                    _ = ProbeAutoLoopAsync();
                    return;
                }

                if (!string.Equals(state.session.phase, "run", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (TryGetAutoLoopRuntime(state, out _))
                {
                    AddLog("INFO", $"Route continuation monitor resumed on screen={state.screen}, available_actions={state.available_actions.Length}.");
                    _ = ProbeAutoLoopAsync();
                    return;
                }

                if (!string.Equals(lastObservedScreen, state.screen, StringComparison.Ordinal))
                {
                    AddLog("INFO", $"Route continuation monitor waiting on screen={state.screen}, available_actions={state.available_actions.Length}.");
                    lastObservedScreen = state.screen;
                }
            }
            catch (Exception ex)
            {
                AddLog("WARN", $"Route continuation monitor probe failed: {ex.Message}");
            }

            await Task.Delay(RouteContinuationProbeInterval).ConfigureAwait(false);
        }

        AddLog("WARN", "Route continuation monitor timed out while waiting for the next route decision window.");
    }


    private static AiRuntimeKind DetermineRuntimeKind(GameStatePayload state)
    {
        return state.in_combat || string.Equals(state.screen, "COMBAT", StringComparison.OrdinalIgnoreCase)
            ? AiRuntimeKind.Combat
            : AiRuntimeKind.Route;
    }

    private static bool TryGetAutoLoopRuntime(GameStatePayload state, out AiRuntimeKind runtimeKind)
    {
        runtimeKind = AiRuntimeKind.Route;

        if (!TryEvaluateStartAvailability(state, out _))
        {
            return false;
        }

        if (state.game_over != null || state.available_actions.Length == 0)
        {
            return false;
        }

        runtimeKind = DetermineRuntimeKind(state);
        if (runtimeKind == AiRuntimeKind.Combat && !state.combat_actions_ready)
        {
            return false;
        }

        return true;
    }

    private void SetInactiveContextStandby(AiRuntimeKind activeRuntime)
    {
        foreach (var pair in _contexts)
        {
            if (pair.Key == activeRuntime)
            {
                continue;
            }

            if (pair.Value.PendingDecision != null || !string.IsNullOrWhiteSpace(pair.Value.Error))
            {
                continue;
            }

            pair.Value.Status = "待命";
        }
    }

    private bool TryGetPendingDecisionLocked(out AiRuntimeKind runtimeKind, out AiDecisionResult decision)
    {
        var activeContext = _contexts[_activeRuntime];
        if (activeContext.PendingDecision?.action.HasAction == true)
        {
            runtimeKind = _activeRuntime;
            decision = activeContext.PendingDecision;
            return true;
        }

        foreach (var pair in _contexts)
        {
            if (pair.Value.PendingDecision?.action.HasAction == true)
            {
                runtimeKind = pair.Key;
                decision = pair.Value.PendingDecision;
                return true;
            }
        }

        runtimeKind = AiRuntimeKind.Route;
        decision = new AiDecisionResult();
        return false;
    }

    private static AiRuntimeContextSnapshot CreateRuntimeSnapshot(AgentRuntimeContext context)
    {
        return new AiRuntimeContextSnapshot
        {
            runtime = FormatRuntimeKind(context.Kind),
            status = context.Status,
            plan_summary = context.PlanSummary,
            reasoning = context.Reasoning,
            pending_action = context.PendingAction,
            has_pending_action = context.PendingDecision?.action.HasAction == true,
            last_action_result = context.LastActionResult,
            error = context.Error,
            recent_notes = context.RecentNotes.ToArray()
        };
    }

    private static string FormatRuntimeKind(AiRuntimeKind runtimeKind)
    {
        return runtimeKind == AiRuntimeKind.Combat ? "combat" : "route";
    }

    private static string FormatRuntimeKindZh(AiRuntimeKind runtimeKind)
    {
        return runtimeKind == AiRuntimeKind.Combat ? "战斗 Agent" : "路线 Agent";
    }

    private static string TranslateScreen(string screen)
    {
        return screen switch
        {
            "COMBAT" => "战斗",
            "MAP" => "地图",
            "EVENT" => "事件",
            "REST" => "休息点",
            "SHOP" => "商店",
            "CHEST" => "宝箱",
            "REWARD" => "奖励",
            "CARD_SELECTION" => "选牌",
            "CHARACTER_SELECT" => "角色选择",
            "GAME_OVER" => "结算",
            "MAIN_MENU" => "主菜单",
            _ => screen
        };
    }

    private static string DescribePlannedAction(GameStatePayload state, AiActionSuggestion action)
    {
        if (action == null || !action.HasAction)
        {
            return "正在观察";
        }

        return action.name.ToLowerInvariant() switch
        {
            "play_card" => $"准备打出{GetCardName(state, action.card_index)}",
            "use_potion" => $"准备使用{GetPotionName(state, action.option_index)}",
            "discard_potion" => $"准备丢弃{GetPotionName(state, action.option_index)}",
            "end_turn" => "准备结束回合",
            "choose_map_node" => $"准备前往{GetMapNodeName(state, action.option_index)}",
            "choose_event_option" => $"准备选择事件选项：{GetEventOptionTitle(state, action.option_index)}",
            "choose_rest_option" => $"准备选择休息选项：{GetRestOptionTitle(state, action.option_index)}",
            "choose_reward_card" => $"准备选择奖励卡牌：{GetRewardCardName(state, action.option_index)}",
            "choose_treasure_relic" => $"准备拿取遗物：{GetTreasureRelicName(state, action.option_index)}",
            "claim_reward" => $"准备领取奖励：{GetRewardName(state, action.option_index)}",
            "buy_card" => $"准备购买卡牌：{GetShopCardName(state, action.option_index)}",
            "buy_relic" => $"准备购买遗物：{GetShopRelicName(state, action.option_index)}",
            "buy_potion" => $"准备购买药水：{GetShopPotionName(state, action.option_index)}",
            "proceed" => "准备继续前进",
            "collect_rewards_and_proceed" => "准备收取奖励并继续前进",
            "open_chest" => "准备打开宝箱",
            _ => AiActionValidator.FormatAction(action)
        };
    }

    private static string DescribeExecutedAction(GameStatePayload state, AiActionSuggestion action)
    {
        if (action == null || !action.HasAction)
        {
            return "正在观察";
        }

        return action.name.ToLowerInvariant() switch
        {
            "play_card" => $"打出了{GetCardName(state, action.card_index)}",
            "use_potion" => $"使用了{GetPotionName(state, action.option_index)}",
            "discard_potion" => $"丢弃了{GetPotionName(state, action.option_index)}",
            "end_turn" => "结束了回合",
            "choose_map_node" => $"前往了{GetMapNodeName(state, action.option_index)}",
            "choose_event_option" => $"选择了事件选项：{GetEventOptionTitle(state, action.option_index)}",
            "choose_rest_option" => $"选择了休息选项：{GetRestOptionTitle(state, action.option_index)}",
            "choose_reward_card" => $"选择了奖励卡牌：{GetRewardCardName(state, action.option_index)}",
            "choose_treasure_relic" => $"拿取了遗物：{GetTreasureRelicName(state, action.option_index)}",
            "claim_reward" => $"领取了奖励：{GetRewardName(state, action.option_index)}",
            "buy_card" => $"购买了卡牌：{GetShopCardName(state, action.option_index)}",
            "buy_relic" => $"购买了遗物：{GetShopRelicName(state, action.option_index)}",
            "buy_potion" => $"购买了药水：{GetShopPotionName(state, action.option_index)}",
            "proceed" => "继续前进",
            "collect_rewards_and_proceed" => "收取奖励并继续前进",
            "open_chest" => "打开了宝箱",
            _ => AiActionValidator.FormatAction(action)
        };
    }

    private static string GetCardName(GameStatePayload state, int? cardIndex)
    {
        if (cardIndex == null || state.combat == null || cardIndex < 0 || cardIndex >= state.combat.hand.Length)
        {
            return "卡牌";
        }

        return state.combat.hand[cardIndex.Value].name;
    }

    private static string GetPotionName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.run == null || optionIndex < 0 || optionIndex >= state.run.potions.Length)
        {
            return "药水";
        }

        return state.run.potions[optionIndex.Value].name ?? "药水";
    }

    private static string GetMapNodeName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.map == null || optionIndex < 0 || optionIndex >= state.map.available_nodes.Length)
        {
            return "地图节点";
        }

        var node = state.map.available_nodes[optionIndex.Value];
        return $"地图节点({node.row},{node.col})/{node.node_type}";
    }

    private static string GetEventOptionTitle(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.@event == null || optionIndex < 0 || optionIndex >= state.@event.options.Length)
        {
            return "事件选项";
        }

        var option = state.@event.options[optionIndex.Value];
        return string.IsNullOrWhiteSpace(option.title) ? option.description : option.title;
    }

    private static string GetRestOptionTitle(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.rest == null || optionIndex < 0 || optionIndex >= state.rest.options.Length)
        {
            return "休息选项";
        }

        return state.rest.options[optionIndex.Value].title;
    }

    private static string GetRewardCardName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.reward == null || optionIndex < 0 || optionIndex >= state.reward.card_options.Length)
        {
            return "奖励卡牌";
        }

        return state.reward.card_options[optionIndex.Value].name;
    }

    private static string GetTreasureRelicName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.chest == null || optionIndex < 0 || optionIndex >= state.chest.relic_options.Length)
        {
            return "遗物";
        }

        return state.chest.relic_options[optionIndex.Value].name;
    }

    private static string GetRewardName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.reward == null || optionIndex < 0 || optionIndex >= state.reward.rewards.Length)
        {
            return "奖励";
        }

        return state.reward.rewards[optionIndex.Value].description;
    }

    private static string GetShopCardName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.shop == null || optionIndex < 0 || optionIndex >= state.shop.cards.Length)
        {
            return "卡牌";
        }

        return state.shop.cards[optionIndex.Value].name;
    }

    private static string GetShopRelicName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.shop == null || optionIndex < 0 || optionIndex >= state.shop.relics.Length)
        {
            return "遗物";
        }

        return state.shop.relics[optionIndex.Value].name;
    }

    private static string GetShopPotionName(GameStatePayload state, int? optionIndex)
    {
        if (optionIndex == null || state.shop == null || optionIndex < 0 || optionIndex >= state.shop.potions.Length)
        {
            return "药水";
        }

        return state.shop.potions[optionIndex.Value].name ?? "药水";
    }

    private void ApplyCombatWaitingStatusLocked(GameStatePayload state)
    {
        _activeRuntime = AiRuntimeKind.Combat;
        var runtimeContext = _contexts[AiRuntimeKind.Combat];
        runtimeContext.Status = TranslateCombatTurnState(state.combat_turn_state);
        runtimeContext.Error = string.Empty;
        if (string.IsNullOrWhiteSpace(runtimeContext.PendingAction))
        {
            runtimeContext.PendingAction = "正在观察";
        }

        _status = runtimeContext.Status;
        _stateSummary = $"界面={TranslateScreen(state.screen)} | 当前 Agent=战斗 Agent | 回合={state.turn?.ToString() ?? "-"} | 战斗阶段={TranslateCombatTurnState(state.combat_turn_state)}";
        SetInactiveContextStandby(AiRuntimeKind.Combat);
    }

    private static string TranslateCombatTurnState(string turnState)
    {
        return turnState switch
        {
            "player_actionable" => "等待玩家行动",
            "enemy_turn" => "敌方回合进行中",
            "turn_transition" => "回合切换中",
            "player_actions_disabled" => "玩家动作暂时锁定",
            "card_animation" => "动作结算动画中",
            "card_selection" => "卡牌选择处理中",
            "hand_mode_transition" => "手牌状态切换中",
            "room_transition" => "战斗房间状态切换中",
            "ui_not_ready" => "战斗界面初始化中",
            "combat_ending" => "战斗即将结束",
            "player_unavailable" => "玩家状态暂不可用",
            _ => "过渡处理中"
        };
    }

    private static AiDecisionResult MaybeApplyCombatTurnFallback(GameStatePayload state, AiRuntimeKind runtimeKind, AiDecisionResult decision)
    {
        if (runtimeKind != AiRuntimeKind.Combat)
        {
            return decision;
        }

        if (state.available_actions.Length == 0 ||
            !state.available_actions.Contains("end_turn", StringComparer.OrdinalIgnoreCase) ||
            decision.action.HasAction)
        {
            return decision;
        }

        var hasPlayableCard = state.combat?.hand.Any(card => card.playable) == true;
        var hasUsablePotion = state.run?.potions.Any(potion => potion.can_use) == true;
        if (hasPlayableCard || hasUsablePotion)
        {
            return decision;
        }

        return new AiDecisionResult
        {
            plan_summary = string.IsNullOrWhiteSpace(decision.plan_summary) ? "没有可继续利用的资源，结束回合。" : decision.plan_summary,
            reasoning = string.IsNullOrWhiteSpace(decision.reasoning)
                ? "当前没有可打出的手牌，也没有适合使用的药水，直接结束回合。"
                : $"{decision.reasoning} 当前无可打出的手牌且无可用药水，改为结束回合。",
            action = new AiActionSuggestion
            {
                name = "end_turn"
            },
            requires_confirmation = false,
            stop_reason = decision.stop_reason,
            safety_checks = decision.safety_checks,
            raw_response = decision.raw_response
        };
    }

    private static AiAgentConfig CreateDisplayConfig(AiAgentConfig config)
    {
        return new AiAgentConfig
        {
            enable_agent = config.enable_agent,
            provider = config.provider,
            base_url = config.base_url,
            model = config.model,
            api_key = AiSecretMasker.Mask(config.api_key),
            temperature = config.temperature,
            auto_execute = config.auto_execute,
            auto_combat_loop = config.auto_combat_loop,
            character_combat_prompts = new Dictionary<string, string>(config.character_combat_prompts, StringComparer.OrdinalIgnoreCase),
            character_route_prompts = new Dictionary<string, string>(config.character_route_prompts, StringComparer.OrdinalIgnoreCase)
        };
    }

    private static string SanitizeForNote(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "(none)"
            : value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private static void AddRuntimeNote(AgentRuntimeContext context, string note)
    {
        context.RecentNotes.Add(note);
        if (context.RecentNotes.Count > MaxRuntimeNotes)
        {
            context.RecentNotes.RemoveAt(0);
        }
    }

    private void AddLog(string level, string message)
    {
        var maskedKey = AiSecretMasker.Mask(_config.api_key);
        var maskedMessage = string.IsNullOrWhiteSpace(_config.api_key)
            ? message
            : message.Replace(_config.api_key, maskedKey, StringComparison.Ordinal);

        lock (_gate)
        {
            _logs.Add(new AiLogEntry(DateTime.UtcNow, level, maskedMessage));
            if (_logs.Count > MaxLogEntries)
            {
                _logs.RemoveRange(0, _logs.Count - MaxLogEntries);
            }
        }

        switch (level)
        {
            case "ERROR":
                Log.Error($"{LogPrefix} {maskedMessage}");
                break;
            case "WARN":
                Log.Warn($"{LogPrefix} {maskedMessage}");
                break;
            default:
                Log.Info($"{LogPrefix} {maskedMessage}");
                break;
        }

        try
        {
            Directory.CreateDirectory(AiRuntimePaths.LogRoot);
            File.AppendAllText(
                AiRuntimePaths.AiLogPath,
                $"[{DateTime.UtcNow:O}] [{level}] {maskedMessage}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed class AgentRuntimeContext
    {
        public AgentRuntimeContext(AiRuntimeKind kind)
        {
            Kind = kind;
        }

        public AiRuntimeKind Kind { get; }

        public string Status { get; set; } = "空闲";

        public string PlanSummary { get; set; } = string.Empty;

        public string Reasoning { get; set; } = string.Empty;

        public string PendingAction { get; set; } = string.Empty;

        public string LastActionResult { get; set; } = string.Empty;

        public string Error { get; set; } = string.Empty;

        public AiDecisionResult? PendingDecision { get; set; }

        public GameStateFingerprint? PendingFingerprint { get; set; }

        public List<string> RecentNotes { get; } = new();
    }
}
