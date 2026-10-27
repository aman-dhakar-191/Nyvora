using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Nyvora.App.Services;
using Nyvora.App.Windowing;
using Nyvora.Core.Animation;
using Nyvora.Core.State;

namespace Nyvora.App;

public sealed partial class MainWindow : Window
{
    private readonly PillStateMachine _machine = new();
    private readonly PillWindowHost _host;
    private readonly DispatcherQueueTimer _timer;
    private readonly Stopwatch _clock = new();

    private SizeD _current = PillAnimator.RestingSize;
    private PillAnimator? _animator;

    public MainWindow()
    {
        InitializeComponent();
        _host = new PillWindowHost(this);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(8); // paced by the compositor; coarse upper bound
        _timer.Tick += OnTick;

        _machine.StateChanged += OnStateChanged;
        Activated += OnActivated;
        Closed += (_, _) =>
        {
            _timer.Stop();
            _host.Dispose();
            Log.Info("Closed");
        };
    }

    public void Show()
    {
        _host.Show();
        UpdateContent();
        Log.Info("Started");
    }

    private void OnRootTapped(object sender, TappedRoutedEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { } src && IsInsideButton(src))
        {
            return;
        }

        _machine.RequestExpand();
    }

    private static bool IsInsideButton(DependencyObject d)
    {
        for (var n = d; n is not null; n = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(n))
        {
            if (n is Microsoft.UI.Xaml.Controls.Button)
            {
                return true;
            }
        }

        return false;
    }

    private void OnEscape(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = _machine.RequestCollapse();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) => _machine.RequestCollapse();

    private void OnQuit(object sender, RoutedEventArgs e) => Close();

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated)
        {
            _machine.RequestCollapse(); // click-away dismissal; no-op when already resting
        }
    }

    private void OnStateChanged(PillState previous, PillState next)
    {
        switch (next)
        {
            case PillState.Expanding:
                _host.SetNoActivate(false); // user-initiated: allow keyboard focus for Esc
                _host.Activate();
                Start(PillAnimator.ExpandedSize, PillAnimator.ExpandDuration);
                break;
            case PillState.Collapsing:
                Start(PillAnimator.RestingSize, PillAnimator.CollapseDuration);
                break;
        }
    }

    private void Start(SizeD target, TimeSpan duration)
    {
        // Begin from the on-screen size so reversing mid-animation is continuous.
        _animator = new PillAnimator(_current, target, duration);
        _clock.Restart();
        _timer.Start();
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_animator is null)
        {
            _timer.Stop();
            return;
        }

        var elapsed = _clock.Elapsed;
        _current = _animator.Sample(elapsed);
        _host.Apply(_current);
        UpdateContent();

        if (_animator.IsComplete(elapsed))
        {
            _timer.Stop();
            _animator = null;
            var generation = _machine.Generation;
            if (_machine.CompleteAnimation(generation) && _machine.State == PillState.Resting)
            {
                _host.SetNoActivate(true);
            }

            UpdateContent();
        }
    }

    /// <summary>Cross-fades resting/expanded content from the current size; no layout depends on window size.</summary>
    private void UpdateContent()
    {
        var t = PillAnimator.ExpansionProgress(_current);
        RestingContent.Opacity = Math.Clamp(1 - t * 3, 0, 1);
        ExpandedContent.Opacity = Math.Clamp((t - 0.45) / 0.55, 0, 1);
        ExpandedContent.IsHitTestVisible = _machine.State is PillState.Expanded;
    }
}
