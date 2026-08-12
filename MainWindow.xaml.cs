using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace DisplayNotify;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _refreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _refreshTimer.Tick += (_, _) => RefreshDisplayStatus();
        Loaded += (_, _) => { RefreshDisplayStatus(); _refreshTimer.Start(); };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshDisplayStatus()
    {
        try
        {
            var displays = DisplayConfigurationReader.GetActiveDisplays();
            DisplayList.ItemsSource = displays;
            var external = displays.Where(display => !display.IsInternal).ToList();

            if (displays.Count == 0)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
                StatusText.Text = "未读取到活动显示器；将在 10 秒后重试";
            }
            else if (external.Count == 0)
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                StatusText.Text = "当前使用内部显示器";
            }
            else
            {
                StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                StatusText.Text = $"检测到 {external.Count} 个外接显示器：{string.Join("、", external.Select(display => display.Connection))}";
            }

            LastCheckedText.Text = $"更新于 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception exception)
        {
            DisplayList.ItemsSource = Array.Empty<DisplayInfo>();
            StatusIndicator.Fill = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            StatusText.Text = $"无法读取显示器连接状态：{exception.Message}";
            LastCheckedText.Text = $"更新失败 {DateTime.Now:HH:mm:ss}";
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDisplayStatus();
    }
}