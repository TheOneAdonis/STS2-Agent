using System.Text;
using Godot;

namespace STS2AIAgent.Agent;

internal sealed partial class AiOverlayWindow : Control
{
    private const float PanelWidth = 440f;
    private const float ExpandedPanelHeight = 312f;
    private const float CollapsedPanelHeight = 48f;
    private const float DefaultMargin = 12f;

    private PanelContainer? _panel;
    private Label? _titleLabel;
    private Button? _startButton;
    private Button? _pauseButton;
    private Button? _collapseButton;
    private RichTextLabel? _summaryText;
    private RichTextLabel? _logsText;
    private VBoxContainer? _contentContainer;
    private bool _collapsed;
    private bool _dragging;
    private bool _positionInitialized;
    private Vector2 _dragOffset;
    private Vector2 _suggestedPosition = new(980f, 72f);

    public bool IsPanelOpen
    {
        get => Visible;
        set => Visible = value;
    }

    public override void _Ready()
    {
        Name = "STS2AIAgentPanel";
        ProcessMode = ProcessModeEnum.Always;
        MouseFilter = MouseFilterEnum.Ignore;
        ZAsRelative = false;
        ZIndex = 4096;
        BuildUi();
        Refresh();
    }

    public override void _Process(double delta)
    {
        Refresh();
    }

    public void TogglePanel()
    {
        Visible = !Visible;
    }

    public void SetSuggestedPosition(Vector2 position)
    {
        _suggestedPosition = position;
        if (!_dragging)
        {
            ApplySuggestedPosition(force: !_positionInitialized);
        }
    }

    private void BuildUi()
    {
        if (_panel != null)
        {
            return;
        }

        _panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Stop,
            Size = new Vector2(PanelWidth, ExpandedPanelHeight)
        };

        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.08f, 0.1f, 0.14f, 0.94f),
            BorderColor = new Color(0.31f, 0.78f, 0.72f, 0.98f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 10,
            CornerRadiusTopRight = 10,
            CornerRadiusBottomLeft = 10,
            CornerRadiusBottomRight = 10,
            ContentMarginLeft = 10,
            ContentMarginTop = 10,
            ContentMarginRight = 10,
            ContentMarginBottom = 10
        };
        _panel.AddThemeStyleboxOverride("panel", panelStyle);

        var root = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        var titleBar = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(0, 30),
            MouseFilter = MouseFilterEnum.Stop
        };
        titleBar.GuiInput += OnTitleGuiInput;

        _titleLabel = new Label
        {
            Text = "LLM",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center
        };

        _startButton = CreateHeaderButton("Start", 58);
        _startButton.Pressed += async () => await AiAgentService.Instance.ResumeAutomationAsync();

        _pauseButton = CreateHeaderButton("Pause", 58);
        _pauseButton.Pressed += AiAgentService.Instance.PauseAutomation;

        var stepButton = CreateHeaderButton("Step", 54);
        stepButton.Pressed += () => AiAgentService.Instance.RequestSingleStep();

        var stopButton = CreateHeaderButton("Stop", 54);
        stopButton.Pressed += () => AiAgentService.Instance.Stop();

        _collapseButton = CreateHeaderButton("-", 28);
        _collapseButton.Pressed += ToggleCollapse;

        titleBar.AddChild(_titleLabel);
        titleBar.AddChild(_startButton);
        titleBar.AddChild(_pauseButton);
        titleBar.AddChild(stepButton);
        titleBar.AddChild(stopButton);
        titleBar.AddChild(_collapseButton);

        _contentContainer = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };

        _summaryText = CreateTextBlock(164);
        _logsText = CreateTextBlock(88);
        _contentContainer.AddChild(_summaryText);
        _contentContainer.AddChild(_logsText);

        root.AddChild(titleBar);
        root.AddChild(_contentContainer);
        _panel.AddChild(root);
        AddChild(_panel);
    }

    private static Button CreateHeaderButton(string text, float width)
    {
        return new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(width, 0),
            MouseFilter = MouseFilterEnum.Stop
        };
    }

    private static RichTextLabel CreateTextBlock(float minHeight)
    {
        return new RichTextLabel
        {
            BbcodeEnabled = false,
            FitContent = false,
            ScrollActive = true,
            SelectionEnabled = true,
            CustomMinimumSize = new Vector2(0, minHeight),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
    }

    private void Refresh()
    {
        if (_panel == null ||
            _titleLabel == null ||
            _startButton == null ||
            _pauseButton == null ||
            _summaryText == null ||
            _logsText == null ||
            _contentContainer == null ||
            _collapseButton == null)
        {
            return;
        }

        ApplySuggestedPosition(force: false);

        var snapshot = AiAgentService.Instance.GetSnapshot();
        if ((snapshot.is_busy || snapshot.has_pending_action) && !Visible)
        {
            Visible = true;
        }

        var activeContext = snapshot.runtime_contexts.FirstOrDefault(context => context.runtime == snapshot.active_runtime);
        _titleLabel.Text = $"LLM {snapshot.active_runtime.ToUpperInvariant()}";
        _startButton.Disabled = snapshot.is_busy || (snapshot.agent_enabled && !snapshot.automation_paused);
        _pauseButton.Disabled = snapshot.automation_paused || !snapshot.agent_enabled;

        var summary = new StringBuilder()
            .AppendLine($"status: {snapshot.status}")
            .AppendLine($"state: {snapshot.state_summary}")
            .AppendLine($"runtime: {snapshot.active_runtime}")
            .AppendLine($"plan: {activeContext?.plan_summary ?? snapshot.plan_summary}")
            .AppendLine($"reason: {activeContext?.reasoning ?? snapshot.reasoning}")
            .AppendLine($"pending: {activeContext?.pending_action ?? snapshot.pending_action}")
            .AppendLine($"last: {activeContext?.last_action_result ?? snapshot.last_action_result}")
            .AppendLine($"agent_enabled: {snapshot.agent_enabled}")
            .AppendLine($"automation_paused: {snapshot.automation_paused}")
            .AppendLine($"auto_combat_loop: {snapshot.auto_combat_loop}");

        var error = activeContext?.error ?? snapshot.error;
        if (!string.IsNullOrWhiteSpace(error))
        {
            summary.AppendLine($"error: {error}");
        }

        var runtimeNotes = activeContext?.recent_notes ?? Array.Empty<string>();
        if (runtimeNotes.Count > 0)
        {
            summary.AppendLine("notes:");
            foreach (var note in runtimeNotes)
            {
                summary.AppendLine($"- {note}");
            }
        }

        _summaryText.Text = summary.ToString().TrimEnd();
        var recentLogs = snapshot.logs.TakeLast(8).Select(log => $"{log.TimestampUtc:HH:mm:ss} {log.Level} {log.Message}");
        _logsText.Text = string.Join('\n', recentLogs);

        _contentContainer.Visible = !_collapsed;
        _collapseButton.Text = _collapsed ? "+" : "-";
        _panel.Size = new Vector2(PanelWidth, _collapsed ? CollapsedPanelHeight : ExpandedPanelHeight);
        ClampToViewport();
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        ClampToViewport();
    }

    private void ApplySuggestedPosition(bool force)
    {
        if (_panel == null || (!force && _positionInitialized))
        {
            return;
        }

        _panel.Position = _suggestedPosition;
        _positionInitialized = true;
        ClampToViewport();
    }

    private void ClampToViewport()
    {
        if (_panel == null)
        {
            return;
        }

        var viewportSize = GetViewportRect().Size;
        var maxX = Mathf.Max(DefaultMargin, viewportSize.X - _panel.Size.X - DefaultMargin);
        var maxY = Mathf.Max(DefaultMargin, viewportSize.Y - _panel.Size.Y - DefaultMargin);
        _panel.Position = new Vector2(
            Mathf.Clamp(_panel.Position.X, DefaultMargin, maxX),
            Mathf.Clamp(_panel.Position.Y, DefaultMargin, maxY));
    }

    private void OnTitleGuiInput(InputEvent @event)
    {
        if (_panel == null)
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
        {
            if (mouseButton.Pressed)
            {
                _dragging = true;
                _dragOffset = GetGlobalMousePosition() - _panel.Position;
            }
            else
            {
                _dragging = false;
            }

            return;
        }

        if (_dragging && @event is InputEventMouseMotion)
        {
            _panel.Position = GetGlobalMousePosition() - _dragOffset;
            ClampToViewport();
        }
    }
}
