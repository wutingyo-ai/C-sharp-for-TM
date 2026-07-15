using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TmTorqueMonitor;

namespace Socket_TCP_Test
{
    public partial class MainWindow : Window
    {
        private TmJointTorqueClient? _client;
        private Chart _chart = null!;

        public MainWindow()
        {
            InitializeComponent();

            _chart = new Chart();
            _chart.UiRefresh = UpdateTorqueTexts;   // Timer 觸發時一起更新文字

            // 若 XAML 的 PlotView 名稱不同，請改成你的 x:Name
            TorquePlotView.Model = _chart.PlotModel;
        }

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            _client = new TmJointTorqueClient();
            _client.TorqueReceived += OnTorqueReceived;
            _client.ErrorOccurred += msg => Dispatcher.Invoke(() => TxtLog.Text = msg);

            try
            {
                await _client.ConnectAsync(TxtIp.Text);
                TxtStatus.Text = "已連線";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"連線失敗：{ex.Message}";
            }
        }

        // 背景執行緒：只存最新一筆，不更新 UI
        private void OnTorqueReceived(JointTorqueSnapshot data)
        {
            _chart.OnTorqueReceived(data);
        }

        // 由 Chart 的 DispatcherTimer 在 UI 執行緒呼叫
        private void UpdateTorqueTexts(JointTorqueSnapshot data)
        {
            TxtJ1.Text = data.J1.ToString("F2");
            TxtJ2.Text = data.J2.ToString("F2");
            TxtJ3.Text = data.J3.ToString("F2");
            TxtJ4.Text = data.J4.ToString("F2");
            TxtJ5.Text = data.J5.ToString("F2");
            TxtJ6.Text = data.J6.ToString("F2");
            TxtTime.Text = data.Timestamp.ToString("HH:mm:ss.fff");
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _client?.Disconnect();
            TxtStatus.Text = "已斷線";
        }

        protected override void OnClosed(EventArgs e)
        {
            _client?.Dispose();
            _chart.Dispose();
            base.OnClosed(e);
        }

        private void IP_Focus(object sender, RoutedEventArgs e)
        {
            if (TxtIp.Text == "Type Your IP:")
            {
                TxtIp.Text = "";
                TxtIp.Foreground = Brushes.Black;
            }
        }

        private void BtnApplyAxis_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(TxtYMin.Text, out double min)) return;
            if (!double.TryParse(TxtYMax.Text, out double max)) return;

            try
            {
                _chart.SetYAxisRange(min, max);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void BtnApplyInterval_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtInterval.Text, out int intervalMs)) return;
            _chart.SetRefreshInterval(intervalMs);  // 文字與圖表一同變慢/變快
        }

        private void BtnResetChart_Click(object sender, RoutedEventArgs e)
        {
            //_chart.Stop();   // 你已有的 Stop
            _chart.Reset();
        }
    }

    public class Chart : IDisposable
    {
        private readonly LineSeries[] _jointSeries = new LineSeries[6];
        private readonly DispatcherTimer _timer = new();
        private JointTorqueSnapshot? _latestSnapshot;
        private double _xIndex = 0;
        private int _maxPoints = 200;
        private int interval = 50;

        public PlotModel PlotModel { get; private set; } = null!;

        /// <summary>與繪圖同一頻率更新 UI 文字（由 MainWindow 指派）</summary>
        public Action<JointTorqueSnapshot>? UiRefresh { get; set; }

        public Chart()
        {
            InitPlot();
            _timer.Tick += OnTimerTick;
            SetRefreshInterval(interval);
            _timer.Start();
        }
        public void Reset()
        {
            foreach (var series in _jointSeries)
                series.Points.Clear();

            _xIndex = 0;
            _latestSnapshot = null;   // 可選：暫停到下一筆資料進來

            var xAxis = (LinearAxis)PlotModel.Axes
                .First(a => a.Position == AxisPosition.Bottom);
            xAxis.Minimum = 0;
            xAxis.Maximum = _maxPoints;

            // 若也要還原 Y 軸（依你需求）
            // var yAxis = ...
            // yAxis.Minimum = -1000;
            // yAxis.Maximum = 1000;

            PlotModel.InvalidatePlot(true);
        }
        private void InitPlot()
        {
            PlotModel = new PlotModel { Title = "Joint Torque" };

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Bottom,
                Title = "Order",
                Minimum = 0,
                Maximum = _maxPoints
            });

            PlotModel.Axes.Add(new LinearAxis
            {
                Position = AxisPosition.Left,
                Title = "Torque",
                Minimum = -50000,
                Maximum = 50000
            });

            OxyColor[] colors =
            {
                OxyColors.Red, OxyColors.Blue, OxyColors.Green,
                OxyColors.Orange, OxyColors.Purple, OxyColors.Brown
            };

            for (int i = 0; i < 6; i++)
            {
                _jointSeries[i] = new LineSeries
                {
                    Title = $"J{i + 1}",
                    Color = colors[i],
                    StrokeThickness = 2
                };
                PlotModel.Series.Add(_jointSeries[i]);
            }
        }

        // 封包進來：只覆蓋最新資料
        public void OnTorqueReceived(JointTorqueSnapshot snapshot)
        {
            _latestSnapshot = snapshot;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (_latestSnapshot == null) return;
            var data = _latestSnapshot;
            double[] values = data.ToArray();
            for (int i = 0; i < 6; i++)
            {
                var series = _jointSeries[i];
                //if (series.Points.Count >= _maxPoints)
                //    series.Points.RemoveAt(0);
                series.Points.Add(new DataPoint(_xIndex, values[i]));
            }
            _xIndex++;
            // 關鍵：可視視窗永遠跟著最新資料
            var xAxis = (LinearAxis)PlotModel.Axes
                .First(a => a.Position == AxisPosition.Bottom); //找出序列中符合在底下軸的那個物件
            if (_xIndex <= _maxPoints)
            {
                xAxis.Minimum = 0;
                xAxis.Maximum = _maxPoints;
            }
            else
            {
                xAxis.Minimum = _xIndex - _maxPoints;
                xAxis.Maximum = _xIndex;
            }
            PlotModel.InvalidatePlot(true);
            UiRefresh?.Invoke(data);
        }

        public void SetYAxisRange(double min, double max)
        {
            if (min >= max)
                throw new ArgumentException("最小值必須小於最大值");

            var yAxis = (LinearAxis)PlotModel.Axes
                .First(a => a.Position == AxisPosition.Left);

            yAxis.Minimum = min;
            yAxis.Maximum = max;
            PlotModel.InvalidatePlot(false);
        }

        public void SetRefreshInterval(int intervalMs)
        {
            if (intervalMs < 1)
                intervalMs = 1;

            _timer.Interval = TimeSpan.FromMilliseconds(intervalMs);
        }

        public void Stop()
        {
            _timer.Stop();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            UiRefresh = null;
        }
    }
}
