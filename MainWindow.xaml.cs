using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using TmTorqueMonitor;

namespace Socket_TCP_Test
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private TmJointTorqueClient? _client;
        public MainWindow()
        {   
            InitializeComponent();
            
        }
        // 連線按鈕
        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            _client = new TmJointTorqueClient();
            // 訂閱事件
            _client.TorqueReceived += OnTorqueReceived;
            _client.ErrorOccurred += msg => Dispatcher.Invoke(() => TxtLog.Text = msg);
            try
            {
                await _client.ConnectAsync(TxtIp.Text);  // 例如 192.168.10.2
                TxtStatus.Text = "已連線";
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"連線失敗：{ex.Message}";
            }
        }
        // 收到力矩（背景執行緒 → 用 Dispatcher 更新 UI）
        private void OnTorqueReceived(JointTorqueSnapshot data)
        {
            Dispatcher.Invoke(() =>
            {
                TxtJ1.Text = data.J1.ToString("F2");
                TxtJ2.Text = data.J2.ToString("F2");
                TxtJ3.Text = data.J3.ToString("F2");
                TxtJ4.Text = data.J4.ToString("F2");
                TxtJ5.Text = data.J5.ToString("F2");
                TxtJ6.Text = data.J6.ToString("F2");
                TxtTime.Text = data.Timestamp.ToString("HH:mm:ss.fff");
            });
        }
        // 斷線按鈕
        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _client?.Disconnect();
            TxtStatus.Text = "已斷線";
        }
        // 關閉視窗時斷線
        protected override void OnClosed(EventArgs e)
        {
            _client?.Dispose();
            base.OnClosed(e);
        }

        private void IP_Focus(object sender, RoutedEventArgs e)
        {
            if (TxtIp.Text == "Type Your IP:")
            {
                TxtIp.Text = "";
                TxtIp.Foreground = Brushes.Black; // 輸入時改回黑色
            }
        }
    }
}