using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DisplayNotify;

public partial class MainWindow : Window
{
    private const double SmallScreenThresholdInches = 20;
    private const double CenterTolerance = 1;
    private static readonly string[] MonitoredProcessNames = ["Ryujinx", "Steam"];
    private readonly DispatcherTimer _refreshTimer;
    private bool _isCloseAuthorized;

    public MainWindow()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => RefreshStatus();
        Loaded += (_, _) => { RefreshStatus(); _refreshTimer.Start(); };
        Closed += (_, _) => _refreshTimer.Stop();
        Closing += MainWindow_Closing;
    }

    private void RefreshStatus()
    {
        var runningApplications = GetRunningMonitoredApplications();
        var warningActive = runningApplications.Count > 0;
        AppTitle.Foreground = new SolidColorBrush(warningActive ? Color.FromRgb(220, 38, 38) : Color.FromRgb(51, 51, 51));

        if (warningActive && WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
            Activate();
        }

        try
        {
            var displays = DisplayConfigurationReader.GetActiveDisplays();
            DisplayList.ItemsSource = displays;
            var external = displays.Where(display => !display.IsInternal).ToList();
            var smallScreenDetected = displays.Any(display => display.DiagonalInches < SmallScreenThresholdInches);
            var protectionActive = warningActive && smallScreenDetected;
            UpdateWindowProtection(protectionActive);

            if (protectionActive)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                StatusText.Text = $"检测到正在运行：{string.Join("、", runningApplications)}；屏幕小于{SmallScreenThresholdInches:0}\"，已启用窗口保护";
            }
            else if (warningActive)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                StatusText.Text = $"检测到正在运行：{string.Join("、", runningApplications)}";
            }
            else if (displays.Count == 0)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                StatusText.Text = "未读取到活动显示器；将在10秒后重试";
            }
            else if (external.Count == 0)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                StatusText.Text = "当前使用内部显示器";
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                StatusText.Text = $"检测到{external.Count}个外接显示器：{string.Join("、", external.Select(display => display.Connection))}";
            }

            LastCheckedText.Text = $"更新于{DateTime.Now:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            UpdateWindowProtection(false);
            DisplayList.ItemsSource = Array.Empty<DisplayInfo>();
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            StatusText.Text = warningActive
                ? $"检测到正在运行：{string.Join("、", runningApplications)}"
                : $"无法读取显示器连接状态：{exception.Message}";
            LastCheckedText.Text = $"更新失败 {DateTime.Now:HH:mm:ss}";
        }
    }

    private void UpdateWindowProtection(bool active)
    {
        ProtectionWarningText.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        Topmost = active;
        ResizeMode = active ? ResizeMode.NoResize : ResizeMode.CanMinimize;

        if (!active || WindowState != WindowState.Normal)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var centeredLeft = workArea.Left + (workArea.Width - ActualWidth) / 2;
        var centeredTop = workArea.Top + (workArea.Height - ActualHeight) / 2;
        if (double.IsNaN(Left) || double.IsNaN(Top) || Math.Abs(Left - centeredLeft) > CenterTolerance || Math.Abs(Top - centeredTop) > CenterTolerance)
        {
            Left = centeredLeft;
            Top = centeredTop;
        }
    }

    private static List<string> GetRunningMonitoredApplications()
    {
        var runningProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var minecraftWindowOpen = false;

        foreach (var process in System.Diagnostics.Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    runningProcessNames.Add(process.ProcessName);
                    minecraftWindowOpen |= process.MainWindowTitle.Contains("Minecraft", StringComparison.OrdinalIgnoreCase);
                }
                catch (InvalidOperationException)
                {
                    // 进程可能在枚举期间退出，下一次刷新会重新检查。
                }
            }
        }

        var runningApplications = MonitoredProcessNames
            .Where(runningProcessNames.Contains)
            .ToList();

        if (minecraftWindowOpen)
        {
            runningApplications.Insert(0, "Minecraft");
        }

        return runningApplications;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isCloseAuthorized)
        {
            return;
        }

        e.Cancel = true;
        if (ShowClosePasswordDialog())
        {
            _isCloseAuthorized = true;
            Dispatcher.BeginInvoke(Close, DispatcherPriority.Send);
        }
    }

    private bool ShowClosePasswordDialog()
    {
        var passwordBox = new System.Windows.Controls.PasswordBox
        {
            PasswordChar = '*',
            FontSize = 15,
            Padding = new Thickness(8),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0)
        };
        var errorText = new System.Windows.Controls.TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var dialog = new Window
        {
            Title = "关闭护眼卫士",
            Owner = this,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = new SolidColorBrush(Color.FromRgb(240, 244, 248)),
            FontFamily = FontFamily
        };
        var confirmButton = new System.Windows.Controls.Button
        {
            Content = "确认",
            Width = 92,
            Height = 30,
            IsDefault = true,
            Background = new SolidColorBrush(Color.FromRgb(64, 158, 255)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 72,
            Height = 30,
            IsCancel = true,
            Margin = new Thickness(8, 0, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(248, 251, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222))
        };

        confirmButton.Click += (_, _) =>
        {
            if (passwordBox.Password == DateTime.Now.ToString("yyyyMMddHHmm"))
            {
                dialog.DialogResult = true;
                return;
            }

            errorText.Text = "验证失败，请重试。";
            passwordBox.Clear();
            passwordBox.Focus();
        };
        cancelButton.Click += (_, _) => dialog.DialogResult = false;

        var buttons = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        buttons.Children.Add(confirmButton);
        buttons.Children.Add(cancelButton);

        var content = new System.Windows.Controls.StackPanel { Margin = new Thickness(18) };
        content.Children.Add(new System.Windows.Controls.TextBlock { Text = "请输入密码：", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var passwordBorder = new System.Windows.Controls.Border
        {
            Height = 34,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = passwordBox
        };
        content.Children.Add(passwordBorder);
        content.Children.Add(errorText);
        content.Children.Add(buttons);
        dialog.Content = new System.Windows.Controls.Border
        {
            Margin = new Thickness(14),
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(208, 215, 222)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = content
        };
        dialog.Loaded += (_, _) => passwordBox.Focus();

        return dialog.ShowDialog() == true;
    }
}
