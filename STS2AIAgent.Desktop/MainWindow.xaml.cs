using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace STS2AIAgent.Desktop;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private static readonly Brush GoodBadgeBackground = CreateBrush("#FFE9F7EF");
    private static readonly Brush GoodBadgeBorder = CreateBrush("#FF2F9E44");
    private static readonly Brush WarmBadgeBackground = CreateBrush("#FFFFF4E5");
    private static readonly Brush WarmBadgeBorder = CreateBrush("#FFB7791F");
    private static readonly Brush CoolBadgeBackground = CreateBrush("#FFEAF4FF");
    private static readonly Brush CoolBadgeBorder = CreateBrush("#FF0071E3");
    private static readonly Brush DangerBadgeBackground = CreateBrush("#FFFFECEB");
    private static readonly Brush DangerBadgeBorder = CreateBrush("#FFD92D20");
    private static readonly Brush IdleBadgeBackground = CreateBrush("#FFF2F2F7");
    private static readonly Brush IdleBadgeBorder = CreateBrush("#1F000000");

    private readonly AgentApiClient _apiClient = new("http://127.0.0.1:8081/");
    private readonly DispatcherTimer _refreshTimer;
    private readonly string _configPath;
    private AgentConfig _draftConfig = new();
    private AgentSnapshot? _latestSnapshot;
    private bool _refreshInFlight;
    private bool _commandInFlight;
    private bool _gameConnected;
    private bool _agentRunning;
    private bool _automationPaused;
    private bool _suppressInputEvents;
    private int _refreshFailureCount;
    private DateTime _statusHintStickyUntilUtc = DateTime.MinValue;
    private string _currentPromptKey = string.Empty;
    private string _currentPromptOwnerName = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "creative-ai",
            "config",
            "in-game-agent.json");

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += async (_, _) => await RefreshSnapshotAsync();

        Loaded += async (_, _) =>
        {
            LoadConfigFromDisk();
            ResetUiForDisconnectedState();
            _refreshTimer.Start();
            await RefreshSnapshotAsync(force: true);
        };
        Closing += (_, _) =>
        {
            _refreshTimer.Stop();
            SaveConfigToDisk(_draftConfig);
        };
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateRequiredInputs(requireGameConnection: false))
        {
            return;
        }

        try
        {
            await RunCommandAsync(async () =>
            {
                var config = CollectManagedConfig(enableAgent: _latestSnapshot?.agent_enabled == true);
                await PersistConfigAsync(config, pushToGame: _gameConnected);
                SetStatusHint(_gameConnected ? "配置已保存并同步到游戏内服务。" : "配置已保存，本次会在游戏连接后生效。", stickySeconds: 6);
            });
        }
        catch (Exception ex)
        {
            SetStatusHint($"保存失败：{ex.Message}", stickySeconds: 8);
        }
    }

    private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateRequiredInputs(requireGameConnection: false))
        {
            return;
        }

        try
        {
            await RunCommandAsync(async () =>
            {
                var config = CollectManagedConfig(enableAgent: _latestSnapshot?.agent_enabled == true);
                await PersistConfigAsync(config, pushToGame: false);
                var result = await _apiClient.TestLlmAsync(config, CancellationToken.None);
                SetStatusHint($"模型连接成功：{result.model}", stickySeconds: 6);
            });
        }
        catch (Exception ex)
        {
            SetStatusHint($"检测失败：{ex.Message}", stickySeconds: 8);
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryValidateRequiredInputs(requireGameConnection: true))
        {
            return;
        }

        try
        {
            await RunCommandAsync(async () =>
            {
                var config = CollectManagedConfig(enableAgent: false);
                await PersistConfigAsync(config, pushToGame: true);
                await _apiClient.StartAsync(CancellationToken.None);
                await RefreshSnapshotAsync(force: true);
                HideInlineNotice();
                SetStatusHint("AI 托管已启动，将根据当前状态自动切换战斗 Agent 或路线 Agent。", stickySeconds: 6);
            });
        }
        catch (Exception ex)
        {
            ShowInlineNotice(ex.Message);
            SetStatusHint($"启动失败：{ex.Message}", stickySeconds: 8);
        }
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await RunCommandAsync(async () =>
            {
                var config = CollectManagedConfig(enableAgent: false);
                await PersistConfigAsync(config, pushToGame: true);
                await _apiClient.StopAsync(CancellationToken.None);
                HideInlineNotice();
                SetStatusHint("AI 托管已停止。", stickySeconds: 6);
                await RefreshSnapshotAsync(force: true);
            });
        }
        catch (Exception ex)
        {
            SetStatusHint($"停止失败：{ex.Message}", stickySeconds: 8);
        }
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BaseConfigChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressInputEvents)
        {
            return;
        }

        _draftConfig.base_url = BaseUrlTextBox.Text.Trim();
        _draftConfig.model = ModelTextBox.Text.Trim();
        UpdateControlStates();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressInputEvents)
        {
            return;
        }

        _draftConfig.api_key = ApiKeyBox.Password.Trim();
        UpdateControlStates();
    }

    private void CombatPromptContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FocusPromptEditor(CombatPromptTextBox);
    }

    private void RoutePromptContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        FocusPromptEditor(RoutePromptTextBox);
    }

    private void PromptTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBox textBox || !textBox.IsEnabled || textBox.IsKeyboardFocusWithin)
        {
            return;
        }

        textBox.Focus();
        var caretIndex = textBox.GetCharacterIndexFromPoint(e.GetPosition(textBox), snapToText: true);
        textBox.CaretIndex = caretIndex >= 0 ? caretIndex : textBox.Text.Length;
        e.Handled = true;
    }

    private void CombatPromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressInputEvents)
        {
            return;
        }

        UpdateCurrentCharacterPrompt(_draftConfig.character_combat_prompts, CombatPromptTextBox.Text);
    }

    private void RoutePromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressInputEvents)
        {
            return;
        }

        UpdateCurrentCharacterPrompt(_draftConfig.character_route_prompts, RoutePromptTextBox.Text);
    }

    private async Task RunCommandAsync(Func<Task> action)
    {
        try
        {
            _commandInFlight = true;
            UpdateControlStates();
            await action();
        }
        finally
        {
            _commandInFlight = false;
            UpdateControlStates();
        }
    }

    private async Task PersistConfigAsync(AgentConfig config, bool pushToGame)
    {
        _draftConfig = config.Clone();
        SaveConfigToDisk(_draftConfig);
        if (pushToGame)
        {
            await _apiClient.SaveConfigAsync(_draftConfig, CancellationToken.None);
        }
    }

    private void SaveConfigToDisk(AgentConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
        File.WriteAllText(_configPath, JsonSerializer.Serialize(config, JsonOptions));
    }

    private void LoadConfigFromDisk()
    {
        try
        {
            _draftConfig = NormalizeConfig(File.Exists(_configPath)
                ? JsonSerializer.Deserialize<AgentConfig>(File.ReadAllText(_configPath), JsonOptions) ?? new AgentConfig()
                : new AgentConfig());
        }
        catch
        {
            _draftConfig = new AgentConfig();
        }

        ApplyDraftToInputs();
    }

    private AgentConfig CollectManagedConfig(bool enableAgent)
    {
        var config = _draftConfig.Clone();
        config.enable_agent = enableAgent;
        config.provider = "openai_compatible";
        config.base_url = BaseUrlTextBox.Text.Trim();
        config.model = ModelTextBox.Text.Trim();
        config.api_key = ApiKeyBox.Password.Trim();
        config.temperature = 0.2d;
        config.auto_execute = true;
        config.auto_combat_loop = true;
        UpdateCurrentCharacterPrompt(config.character_combat_prompts, CombatPromptTextBox.Text);
        UpdateCurrentCharacterPrompt(config.character_route_prompts, RoutePromptTextBox.Text);
        return NormalizeConfig(config);
    }

    private void ApplyDraftToInputs()
    {
        _suppressInputEvents = true;
        BaseUrlTextBox.Text = string.IsNullOrWhiteSpace(_draftConfig.base_url)
            ? "https://api.openai.com/v1"
            : _draftConfig.base_url;
        ModelTextBox.Text = string.IsNullOrWhiteSpace(_draftConfig.model)
            ? "gpt-4.1-mini"
            : _draftConfig.model;
        ApiKeyBox.Password = _draftConfig.api_key ?? string.Empty;
        _suppressInputEvents = false;
        SyncPromptEditors(forceReload: true);
        UpdateControlStates();
    }

    private bool TryValidateRequiredInputs(bool requireGameConnection)
    {
        if (string.IsNullOrWhiteSpace(BaseUrlTextBox.Text) ||
            string.IsNullOrWhiteSpace(ModelTextBox.Text) ||
            string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            SetStatusHint("请先填写 Base URL、模型和 API Key。", stickySeconds: 6);
            return false;
        }

        if (requireGameConnection && !_gameConnected)
        {
            SetStatusHint("尚未连接到游戏内 mod 服务，请先启动游戏并确认模组已载入。", stickySeconds: 6);
            return false;
        }

        return true;
    }

    private async Task RefreshSnapshotAsync(bool force = false)
    {
        if (_refreshInFlight && !force)
        {
            return;
        }

        _refreshInFlight = true;
        try
        {
            var health = await _apiClient.GetHealthAsync(CancellationToken.None);
            _gameConnected = true;
            _refreshFailureCount = 0;
            RenderHealth(health);

            var snapshot = await _apiClient.GetSnapshotAsync(CancellationToken.None);
            _latestSnapshot = snapshot;
            RenderSnapshot(snapshot);
        }
        catch (Exception ex)
        {
            _refreshFailureCount++;
            if (_refreshFailureCount >= 2)
            {
                _gameConnected = false;
                _agentRunning = false;
                _automationPaused = false;
                _latestSnapshot = null;
                RenderDisconnected(ex.Message);
            }
        }
        finally
        {
            _refreshInFlight = false;
            UpdateControlStates();
        }
    }

    private void RenderHealth(ServiceHealthPayload health)
    {
        SetBadge(GameStatusBadge, GameStatusTextBlock, "游戏已连接", GoodBadgeBackground, GoodBadgeBorder);
        if (DateTime.UtcNow >= _statusHintStickyUntilUtc)
        {
            SetStatusHintSilently($"已连接游戏内服务 · 协议 {health.protocol_version} · 游戏 {health.game_version}");
        }
    }

    private void RenderSnapshot(AgentSnapshot snapshot)
    {
        _agentRunning = IsAgentRunning(snapshot);
        _automationPaused = snapshot.automation_paused;

        SetBadge(
            AgentStatusBadge,
            AgentStatusTextBlock,
            ResolveAiStatus(snapshot),
            ResolveAiBadgeBackground(snapshot),
            ResolveAiBadgeBorder(snapshot));

        CharacterNameTextBlock.Text = ResolveCharacterDisplayName(snapshot);
        CharacterIdTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.current_character_id)
            ? "当前角色提示词会在进入对局后自动切换。"
            : $"角色 ID：{snapshot.current_character_id}";
        ScreenPhaseTextBlock.Text = $"当前界面：{TranslateScreen(snapshot.current_screen)}\n当前阶段：{TranslatePhase(snapshot.session_phase)}";
        PromptOwnerTextBlock.Text = ResolvePromptOwnerText(snapshot);
        var canStart = CanStartFromSnapshot(snapshot);
        if (canStart || snapshot.agent_enabled)
        {
            HideInlineNotice();
        }

        EntryGuardTextBlock.Text = canStart
            ? (_agentRunning ? "AI 正在托管本局爬塔。" : snapshot.agent_enabled ? "AI 已启动，正在等待下一次可执行决策。" : "已进入对局，可开始托管。")
            : Coalesce(snapshot.start_block_reason, "请先进入一局爬塔后再启动托管。");
        EditHintTextBlock.Text = _agentRunning
            ? "AI 爬塔中，提示词已锁定"
            : snapshot.agent_enabled
                ? "AI 已启动，等待进入可执行状态"
                : string.IsNullOrWhiteSpace(_currentPromptKey)
                    ? "进入任意角色后即可编辑提示词"
                    : "未运行，可编辑当前角色提示词";

        SyncPromptEditors(forceReload: false);
        RenderActiveContext(snapshot);

        if (!string.IsNullOrWhiteSpace(snapshot.error))
        {
            SetStatusHint($"当前错误：{snapshot.error}", stickySeconds: 8);
        }
        else if (DateTime.UtcNow >= _statusHintStickyUntilUtc)
        {
            SetStatusHintSilently(canStart
                ? (_agentRunning ? "AI 托管中，会自动在战斗 Agent 与路线 Agent 间切换。" : snapshot.agent_enabled ? "AI 已启动，等待下一次可执行决策。" : "已进入对局，随时可以开始托管。")
                : Coalesce(snapshot.start_block_reason, "请先进入一局爬塔后再启动托管。"));
        }
    }

    private void RenderActiveContext(AgentSnapshot snapshot)
    {
        var context = snapshot.runtime_contexts.FirstOrDefault(item =>
            string.Equals(item.runtime, snapshot.active_runtime, StringComparison.OrdinalIgnoreCase));
        if (context == null)
        {
            ActiveAgentTitleTextBlock.Text = "等待中";
            AgentPlanTextBlock.Text = "暂无计划";
            AgentReasonTextBlock.Text = "暂无推理";
            AgentActionTextBlock.Text = "正在观察";
            AgentErrorTextBlock.Text = Coalesce(snapshot.error, "当前没有错误。");
            return;
        }

        var runtimeLabel = string.Equals(context.runtime, "combat", StringComparison.OrdinalIgnoreCase)
            ? "正在战斗"
            : "正在爬塔";
        ActiveAgentTitleTextBlock.Text = runtimeLabel;
        AgentPlanTextBlock.Text = Coalesce(context.plan_summary, "暂无计划");
        AgentReasonTextBlock.Text = Coalesce(context.reasoning, "暂无推理");
        AgentActionTextBlock.Text = BuildRuntimeAction(context);
        AgentErrorTextBlock.Text = Coalesce(context.error, "当前没有错误。");
    }

    private void RenderDisconnected(string message)
    {
        SetBadge(GameStatusBadge, GameStatusTextBlock, "等待连接", WarmBadgeBackground, WarmBadgeBorder);
        SetBadge(AgentStatusBadge, AgentStatusTextBlock, "离线", IdleBadgeBackground, IdleBadgeBorder);
        ResetUiForDisconnectedState();
        HideInlineNotice();
        SetStatusHintSilently($"未检测到游戏内 mod 服务：{message}");
    }

    private void ResetUiForDisconnectedState()
    {
        CharacterNameTextBlock.Text = "等待进入对局";
        CharacterIdTextBlock.Text = "当前角色提示词会在进入对局后自动切换。";
        ScreenPhaseTextBlock.Text = "当前界面：主菜单\n当前阶段：菜单阶段";
        PromptOwnerTextBlock.Text = ResolvePromptOwnerText(snapshot: null);
        EntryGuardTextBlock.Text = "等待进入一局对局后再启动托管。";
        EditHintTextBlock.Text = string.IsNullOrWhiteSpace(_currentPromptKey)
            ? "进入任意角色后即可编辑提示词"
            : "未连接游戏，仍可编辑上次角色提示词";
        ActiveAgentTitleTextBlock.Text = "等待中";
        AgentPlanTextBlock.Text = "暂无计划";
        AgentReasonTextBlock.Text = "暂无推理";
        AgentActionTextBlock.Text = "正在观察";
        AgentErrorTextBlock.Text = "当前没有错误。";
        HideInlineNotice();
        SyncPromptEditors(forceReload: true);
    }

    private void SyncPromptEditors(bool forceReload)
    {
        var promptKey = GetPromptKey(_latestSnapshot);
        var keyChanged = false;
        if (!string.IsNullOrWhiteSpace(promptKey))
        {
            keyChanged = !string.Equals(_currentPromptKey, promptKey, StringComparison.OrdinalIgnoreCase);
            _currentPromptKey = promptKey;
            _currentPromptOwnerName = ResolveCharacterDisplayName(_latestSnapshot!);
        }

        var combatPrompt = ReadPrompt(_draftConfig.character_combat_prompts, _currentPromptKey);
        var routePrompt = ReadPrompt(_draftConfig.character_route_prompts, _currentPromptKey);
        if (forceReload || keyChanged || !CombatPromptTextBox.IsKeyboardFocusWithin)
        {
            SetTextBoxText(CombatPromptTextBox, combatPrompt);
        }

        if (forceReload || keyChanged || !RoutePromptTextBox.IsKeyboardFocusWithin)
        {
            SetTextBoxText(RoutePromptTextBox, routePrompt);
        }
    }

    private void UpdateCurrentCharacterPrompt(Dictionary<string, string> promptMap, string value)
    {
        var promptKey = _currentPromptKey;
        if (string.IsNullOrWhiteSpace(promptKey))
        {
            UpdateControlStates();
            return;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            promptMap.Remove(promptKey);
        }
        else
        {
            promptMap[promptKey] = value.Trim();
        }

        UpdateControlStates();
    }

    private void UpdateControlStates()
    {
        var hasRequiredConfig =
            !string.IsNullOrWhiteSpace(BaseUrlTextBox.Text) &&
            !string.IsNullOrWhiteSpace(ModelTextBox.Text) &&
            !string.IsNullOrWhiteSpace(ApiKeyBox.Password);
        var canEditPrompts = !_commandInFlight && !_agentRunning && !string.IsNullOrWhiteSpace(_currentPromptKey);
        var agentEnabled = _latestSnapshot?.agent_enabled == true;

        BaseUrlTextBox.IsEnabled = !_commandInFlight && !_agentRunning;
        ModelTextBox.IsEnabled = !_commandInFlight && !_agentRunning;
        ApiKeyBox.IsEnabled = !_commandInFlight && !_agentRunning;
        CombatPromptTextBox.IsEnabled = canEditPrompts;
        RoutePromptTextBox.IsEnabled = canEditPrompts;

        SaveButton.IsEnabled = !_commandInFlight && !_agentRunning && hasRequiredConfig;
        TestConnectionButton.IsEnabled = !_commandInFlight && hasRequiredConfig;
        StartButton.IsEnabled = !_commandInFlight && _gameConnected && !agentEnabled && hasRequiredConfig;
        StopButton.IsEnabled = !_commandInFlight && _gameConnected && (agentEnabled || _automationPaused);
        StartButton.Content = "开始托管";
    }

    private void SetStatusHint(string message, int stickySeconds = 0)
    {
        StatusHintTextBlock.Text = message;
        _statusHintStickyUntilUtc = stickySeconds > 0
            ? DateTime.UtcNow.AddSeconds(stickySeconds)
            : DateTime.MinValue;
    }

    private void SetStatusHintSilently(string message)
    {
        if (!string.Equals(StatusHintTextBlock.Text, message, StringComparison.Ordinal))
        {
            StatusHintTextBlock.Text = message;
        }
    }

    private void ShowInlineNotice(string message)
    {
        InlineNoticeTextBlock.Text = message;
        InlineNoticeBorder.Visibility = Visibility.Visible;
    }

    private void HideInlineNotice()
    {
        InlineNoticeBorder.Visibility = Visibility.Collapsed;
    }

    private static void SetBadge(Border badge, TextBlock textBlock, string text, Brush background, Brush border)
    {
        badge.Background = background;
        badge.BorderBrush = border;
        textBlock.Text = text;
    }

    private static bool IsAgentRunning(AgentSnapshot snapshot)
    {
        if (IsWaitingForRun(snapshot))
        {
            return false;
        }

        return snapshot.agent_enabled &&
            !snapshot.automation_paused &&
            !snapshot.status.Contains("Stopped", StringComparison.OrdinalIgnoreCase) &&
            !snapshot.status.Contains("Disabled", StringComparison.OrdinalIgnoreCase) &&
            !snapshot.status.Contains("已停止", StringComparison.OrdinalIgnoreCase) &&
            !snapshot.status.Contains("未启动", StringComparison.OrdinalIgnoreCase) &&
            !snapshot.status.Contains("等待进入游戏", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveAiStatus(AgentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.error))
        {
            return "出错";
        }

        if (snapshot.automation_paused)
        {
            return "已暂停";
        }

        if (IsWaitingForRun(snapshot))
        {
            return "等待进入游戏";
        }

        if (IsAgentRunning(snapshot))
        {
            return "AI 爬塔中";
        }

        return snapshot.agent_enabled ? TranslateStatus(Coalesce(snapshot.status, "就绪")) : "未启动";
    }

    private static Brush ResolveAiBadgeBackground(AgentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.error))
        {
            return DangerBadgeBackground;
        }

        if (snapshot.automation_paused)
        {
            return WarmBadgeBackground;
        }

        if (IsAgentRunning(snapshot))
        {
            return CoolBadgeBackground;
        }

        return IdleBadgeBackground;
    }

    private static Brush ResolveAiBadgeBorder(AgentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.error))
        {
            return DangerBadgeBorder;
        }

        if (snapshot.automation_paused)
        {
            return WarmBadgeBorder;
        }

        if (IsAgentRunning(snapshot))
        {
            return CoolBadgeBorder;
        }

        return IdleBadgeBorder;
    }

    private static string BuildRuntimeAction(RuntimeContextSnapshot? context)
    {
        if (context == null)
        {
            return "正在观察";
        }

        if (!string.IsNullOrWhiteSpace(context.pending_action))
        {
            return context.pending_action;
        }

        if (!string.IsNullOrWhiteSpace(context.last_action_result))
        {
            return context.last_action_result;
        }

        return context.recent_notes.Count > 0 ? "正在观察" : "正在观察";
    }

    private static string ResolveCharacterDisplayName(AgentSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.current_character_name))
        {
            return snapshot.current_character_name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(snapshot.current_character_id))
        {
            return snapshot.current_character_id.Trim();
        }

        return "等待进入对局";
    }

    private static string GetPromptKey(AgentSnapshot? snapshot)
    {
        if (snapshot == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.current_character_id))
        {
            return snapshot.current_character_id.Trim();
        }

        return snapshot.current_character_name?.Trim() ?? string.Empty;
    }

    private static bool CanStartFromSnapshot(AgentSnapshot? snapshot)
    {
        return snapshot?.can_start_automation == true;
    }

    private static bool IsWaitingForRun(AgentSnapshot snapshot)
    {
        return snapshot.agent_enabled &&
            !snapshot.automation_paused &&
            !CanStartFromSnapshot(snapshot);
    }

    private static string ReadPrompt(IReadOnlyDictionary<string, string> promptMap, string promptKey)
    {
        if (string.IsNullOrWhiteSpace(promptKey))
        {
            return string.Empty;
        }

        return promptMap.TryGetValue(promptKey, out var value) ? value : string.Empty;
    }

    private void SetTextBoxText(TextBox textBox, string value)
    {
        value ??= string.Empty;
        if (string.Equals(textBox.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _suppressInputEvents = true;
        textBox.Text = value;
        _suppressInputEvents = false;
    }

    private static string Coalesce(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string TranslateStatus(string value)
    {
        return value switch
        {
            "Idle" => "空闲",
            "Ready" => "就绪",
            "Disabled by config" => "未启动",
            "Stopped" => "已停止",
            "Paused" => "已暂停",
            "Error" => "出错",
            "Execution error" => "执行出错",
            "Cancelled" => "已取消",
            "Suggestion ready" => "建议已生成",
            "No action suggested" => "正在观察",
            "Waiting for confirmation" => "等待确认",
            "Action finished" => "执行完成",
            "Auto action finished" => "自动执行完成",
            _ => value
        };
    }

    private static string TranslateScreen(string value)
    {
        return value switch
        {
            "COMBAT" => "战斗",
            "MAP" => "地图",
            "EVENT" => "事件",
            "REST" => "休息点",
            "SHOP" => "商店",
            "CHEST" => "宝箱",
            "REWARD" => "奖励",
            "CARD_SELECTION" => "选牌",
            "MAIN_MENU" => "主菜单",
            "CHARACTER_SELECT" => "角色选择",
            _ => string.IsNullOrWhiteSpace(value) ? "未知界面" : value
        };
    }

    private static string TranslatePhase(string value)
    {
        return value switch
        {
            "run" => "对局中",
            "menu" => "菜单阶段",
            "lobby" => "大厅阶段",
            "character_select" => "角色选择",
            "multiplayer_lobby" => "多人大厅",
            _ => string.IsNullOrWhiteSpace(value) ? "未知阶段" : value
        };
    }

    private string ResolvePromptOwnerText(AgentSnapshot? snapshot)
    {
        if (!string.IsNullOrWhiteSpace(GetPromptKey(snapshot)))
        {
            return $"正在编辑：{ResolveCharacterDisplayName(snapshot!)} 的专属提示词";
        }

        if (!string.IsNullOrWhiteSpace(_currentPromptKey))
        {
            var ownerName = string.IsNullOrWhiteSpace(_currentPromptOwnerName) ? _currentPromptKey : _currentPromptOwnerName;
            return $"当前未检测到角色，继续编辑：{ownerName} 的专属提示词";
        }

        return "未检测到当前角色提示词。";
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static void FocusPromptEditor(TextBox textBox)
    {
        if (!textBox.IsEnabled)
        {
            return;
        }

        textBox.Focus();
        Keyboard.Focus(textBox);
        if (textBox.CaretIndex < 0)
        {
            textBox.CaretIndex = textBox.Text.Length;
        }
    }

    private static AgentConfig NormalizeConfig(AgentConfig config)
    {
        config.provider ??= "openai_compatible";
        config.base_url ??= "https://api.openai.com/v1";
        config.model ??= "gpt-4.1-mini";
        config.api_key ??= string.Empty;
        config.character_combat_prompts ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        config.character_route_prompts ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return config;
    }
}
