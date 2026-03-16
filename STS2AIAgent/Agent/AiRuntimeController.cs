using System.IO;
using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.TopBar;

namespace STS2AIAgent.Agent;

internal sealed partial class AiRuntimeController : Node
{
    private const string LogPrefix = "[STS2AIAgent.UI]";

    private AiOverlayWindow? _overlayWindow;
    private AiTopBarButton? _topBarButton;
    private string _lastOverlayHost = string.Empty;
    private string _lastButtonHost = string.Empty;
    private Vector2 _lastButtonPosition = new(float.MinValue, float.MinValue);
    private bool _reportedMissingTopBar;
    private bool _reportedMissingOverlayHost;
    private bool _reportedReady;
    private bool _initialized;

    public void Pump()
    {
        EnsureInitialized();
        EnsureUiMounted();
        SyncOverlayPosition();
    }

    public void DisposeUi()
    {
        if (_overlayWindow != null && GodotObject.IsInstanceValid(_overlayWindow))
        {
            _overlayWindow.QueueFree();
        }

        if (_topBarButton != null && GodotObject.IsInstanceValid(_topBarButton))
        {
            _topBarButton.QueueFree();
        }

        _overlayWindow = null;
        _topBarButton = null;
    }

    private void OnTopBarButtonPressed()
    {
        _overlayWindow?.TogglePanel();
        SyncOverlayPosition(force: true);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Name = "STS2AIAgentRuntimeController";
        _overlayWindow ??= new AiOverlayWindow { Visible = false };
        _topBarButton ??= new AiTopBarButton();
        _topBarButton.Pressed -= OnTopBarButtonPressed;
        _topBarButton.Pressed += OnTopBarButtonPressed;
        _initialized = true;

        if (!_reportedReady)
        {
            _reportedReady = true;
            LogUiDiagnostic("Runtime UI controller initialized via service pump.");
        }
    }

    private void EnsureUiMounted()
    {
        _overlayWindow ??= new AiOverlayWindow { Visible = false };
        _topBarButton ??= new AiTopBarButton();

        EnsureTopBarButtonMounted();
        EnsureOverlayMounted();
    }

    private void EnsureTopBarButtonMounted()
    {
        if (_topBarButton == null || !GodotObject.IsInstanceValid(_topBarButton))
        {
            return;
        }

        var topBar = ResolveTopBar();
        if (topBar == null)
        {
            if (!_reportedMissingTopBar)
            {
                _reportedMissingTopBar = true;
                LogUiDiagnostic("Top bar not found yet; button mount deferred.");
            }

            return;
        }

        _reportedMissingTopBar = false;

        var host = (Node)topBar;
        if (host == null || !GodotObject.IsInstanceValid(host))
        {
            return;
        }

        if (_topBarButton.GetParent() != host)
        {
            if (_topBarButton.GetParent() == null)
            {
                host.AddChild(_topBarButton);
            }
            else
            {
                _topBarButton.Reparent(host);
            }
        }

        PlaceTopBarButton(topBar);

        var hostDescription = DescribeNode(host);
        if (!string.Equals(_lastButtonHost, hostDescription, StringComparison.Ordinal))
        {
            _lastButtonHost = hostDescription;
            LogUiDiagnostic($"Mounted top bar button under {hostDescription}");
        }
    }

    private void EnsureOverlayMounted()
    {
        if (_overlayWindow == null || !GodotObject.IsInstanceValid(_overlayWindow))
        {
            return;
        }

        var host = ResolveOverlayHost();
        if (host == null || !GodotObject.IsInstanceValid(host))
        {
            if (!_reportedMissingOverlayHost)
            {
                _reportedMissingOverlayHost = true;
                LogUiDiagnostic("Overlay host not found yet; overlay mount deferred.");
            }

            return;
        }

        _reportedMissingOverlayHost = false;

        if (_overlayWindow.GetParent() != host)
        {
            if (_overlayWindow.GetParent() == null)
            {
                host.AddChild(_overlayWindow);
            }
            else
            {
                _overlayWindow.Reparent(host);
            }
        }

        _overlayWindow.MoveToFront();

        var hostDescription = DescribeNode(host);
        if (!string.Equals(_lastOverlayHost, hostDescription, StringComparison.Ordinal))
        {
            _lastOverlayHost = hostDescription;
            Log.Info($"{LogPrefix} Mounted overlay under {hostDescription}");
        }
    }

    private void SyncOverlayPosition(bool force = false)
    {
        if (_overlayWindow == null ||
            !GodotObject.IsInstanceValid(_overlayWindow) ||
            _topBarButton == null ||
            !GodotObject.IsInstanceValid(_topBarButton) ||
            _overlayWindow.GetParent() is not CanvasItem overlayHost)
        {
            return;
        }

        var buttonRect = _topBarButton.GetGlobalRect();
        var viewportRect = _overlayWindow.GetViewportRect();
        var globalPosition = new Vector2(
            Mathf.Max(12f, viewportRect.Position.X + viewportRect.Size.X - 460f),
            buttonRect.Position.Y + buttonRect.Size.Y + 10f);
        var localPosition = overlayHost.GetGlobalTransformWithCanvas().AffineInverse() * globalPosition;
        _overlayWindow.SetSuggestedPosition(localPosition);

        if (force && !_overlayWindow.IsPanelOpen)
        {
            _overlayWindow.IsPanelOpen = true;
        }
    }

    private void PlaceTopBarButton(NTopBar topBar)
    {
        if (_topBarButton == null || _topBarButton.GetParent() is not CanvasItem buttonHost)
        {
            return;
        }

        var mapButton = topBar.Map as Control;
        var pauseButton = topBar.Pause as Control;
        var anchor = mapButton ?? pauseButton ?? topBar;
        if (anchor == null || !GodotObject.IsInstanceValid(anchor))
        {
            return;
        }

        var desiredPosition = anchor.Position;
        if (mapButton != null && GodotObject.IsInstanceValid(mapButton))
        {
            desiredPosition = mapButton.Position - new Vector2(_topBarButton.Size.X + 14f, 0f);
        }
        else if (pauseButton != null && GodotObject.IsInstanceValid(pauseButton))
        {
            desiredPosition = pauseButton.Position - new Vector2((_topBarButton.Size.X * 2f) + 18f, 0f);
        }

        _topBarButton.SyncFromAnchor(anchor, buttonHost, desiredPosition);

        var localPosition = _topBarButton.Position;
        if (_lastButtonPosition.DistanceTo(localPosition) > 1f)
        {
            _lastButtonPosition = localPosition;
            LogUiDiagnostic(
                $"Positioned AI top bar button at {localPosition.X:0.##},{localPosition.Y:0.##} " +
                $"anchor_local={_topBarButton.LastAnchorLocalPosition.X:0.##},{_topBarButton.LastAnchorLocalPosition.Y:0.##}");
        }
    }

    private static NTopBar? ResolveTopBar()
    {
        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen() as Node;
        if (currentScreen != null)
        {
            var localTopBar = FindDescendant<NTopBar>(currentScreen);
            if (localTopBar != null && localTopBar.IsInsideTree())
            {
                return localTopBar;
            }
        }

        var game = NGame.Instance;
        if (game != null)
        {
            var gameTopBar = FindDescendant<NTopBar>(game);
            if (gameTopBar != null && gameTopBar.IsInsideTree())
            {
                return gameTopBar;
            }
        }

        return null;
    }

    private static Node? ResolveOverlayHost()
    {
        var topBar = ResolveTopBar();
        if (topBar?.GetParent() is Control topBarParent && topBarParent.IsInsideTree())
        {
            return topBarParent;
        }

        var currentScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        if (currentScreen is NCombatRoom combatRoom)
        {
            if (combatRoom.Ui != null && GodotObject.IsInstanceValid(combatRoom.Ui) && combatRoom.Ui.IsInsideTree())
            {
                return combatRoom.Ui;
            }

            if (combatRoom.IsInsideTree())
            {
                return combatRoom;
            }
        }

        if (currentScreen is Control screenControl && screenControl.IsInsideTree())
        {
            return screenControl;
        }

        var game = NGame.Instance;
        if (game?.RootSceneContainer != null && GodotObject.IsInstanceValid(game.RootSceneContainer) && game.RootSceneContainer.IsInsideTree())
        {
            return game.RootSceneContainer;
        }

        var root = game?.GetTree()?.Root;
        return root != null && GodotObject.IsInstanceValid(root) ? root : null;
    }

    private static T? FindDescendant<T>(Node root) where T : class
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T target)
            {
                return target;
            }

            var nested = FindDescendant<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private static string DescribeNode(Node node)
    {
        return $"{node.GetType().FullName}:{node.GetPath()}";
    }

    private static void LogUiDiagnostic(string message)
    {
        Log.Info($"{LogPrefix} {message}");

        try
        {
            Directory.CreateDirectory(AiRuntimePaths.LogRoot);
            File.AppendAllText(
                AiRuntimePaths.UiLogPath,
                $"[{DateTime.UtcNow:O}] {message}{System.Environment.NewLine}");
        }
        catch
        {
        }
    }

    private sealed partial class AiTopBarButton : Control
    {
        private Control? _visual;
        private Button? _hitTarget;
        private Label? _caption;
        private string _anchorSignature = string.Empty;

        public Vector2 LastAnchorLocalPosition { get; private set; }

        public event Action? Pressed;

        public override void _Ready()
        {
            Name = "STS2AIAgentTopBarButton";
            MouseFilter = MouseFilterEnum.Stop;
            CustomMinimumSize = new Vector2(48f, 48f);
            Size = new Vector2(48f, 48f);
            TooltipText = "STS2 AI Agent";
            ZAsRelative = false;
            ZIndex = 4096;
            EnsureChrome();
        }

        public void SyncFromAnchor(Control anchor, CanvasItem host, Vector2 desiredPosition)
        {
            EnsureChrome();
            EnsureVisual(anchor);

            var anchorOrigin = host.GetGlobalTransformWithCanvas().AffineInverse() * anchor.GetGlobalTransformWithCanvas().Origin;
            LastAnchorLocalPosition = anchorOrigin;

            Size = anchor.Size;
            CustomMinimumSize = anchor.Size;
            Position = desiredPosition;

            if (_visual != null)
            {
                _visual.Position = Vector2.Zero;
                _visual.Size = Size;
                _visual.CustomMinimumSize = Size;
            }

            MoveToFront();
        }

        private void EnsureChrome()
        {
            if (_hitTarget == null)
            {
                _hitTarget = new Button
                {
                    Name = "HitTarget",
                    Flat = true,
                    Text = string.Empty,
                    MouseFilter = MouseFilterEnum.Stop,
                    FocusMode = FocusModeEnum.None,
                    Modulate = new Color(1f, 1f, 1f, 0.02f)
                };
                _hitTarget.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                _hitTarget.Pressed += () => Pressed?.Invoke();
                AddChild(_hitTarget);
            }

            if (_caption == null)
            {
                _caption = new Label
                {
                    Name = "AiCaption",
                    Text = "AI",
                    MouseFilter = MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    ThemeTypeVariation = "HeaderSmall"
                };
                _caption.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
                _caption.AddThemeColorOverride("font_color", new Color(0.95f, 1f, 0.98f, 1f));
                _caption.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.3f, 0.24f, 1f));
                _caption.AddThemeConstantOverride("outline_size", 2);
                AddChild(_caption);
                MoveChild(_caption, GetChildCount() - 1);
            }
        }

        private void EnsureVisual(Control anchor)
        {
            var signature = $"{anchor.GetType().FullName}:{anchor.GetPath()}";
            if (_visual != null && string.Equals(_anchorSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            _anchorSignature = signature;

            if (_visual != null && GodotObject.IsInstanceValid(_visual))
            {
                _visual.QueueFree();
                _visual = null;
            }

            if (anchor.Duplicate() is not Control duplicate)
            {
                return;
            }

            duplicate.Name = "Visual";
            duplicate.Position = Vector2.Zero;
            duplicate.Scale = Vector2.One;
            duplicate.MouseFilter = MouseFilterEnum.Ignore;
            duplicate.FocusMode = FocusModeEnum.None;
            DisableInteractionRecursive(duplicate);
            AddChild(duplicate);
            MoveChild(duplicate, 0);
            _visual = duplicate;
        }

        private static void DisableInteractionRecursive(Node node)
        {
            if (node is Control control)
            {
                control.MouseFilter = MouseFilterEnum.Ignore;
                control.FocusMode = FocusModeEnum.None;
            }

            foreach (Node child in node.GetChildren())
            {
                DisableInteractionRecursive(child);
            }
        }
    }
}
