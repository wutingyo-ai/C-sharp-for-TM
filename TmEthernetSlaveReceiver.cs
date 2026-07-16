using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TmTorqueMonitor
{
    /// <summary>
    /// 達明 Ethernet Slave 接收 Joint_Torque 的單一類別
    /// Port 預設 5891，機器人需啟用 Ethernet Slave 並勾選 Joint_Torque
    /// </summary>
    public class TmJointTorqueClient : IDisposable
    {
        private TcpClient? _client; //C# 標準的 TCP 通訊客戶端類別。
        private NetworkStream? _stream; //用來讀取和寫入 TCP 數據流的管道。
        private CancellationTokenSource? _cts; //用來發送「停止接收」訊號給背景執行緒。
        private readonly StringBuilder _buffer = new StringBuilder();

        /// <summary>預設 Ethernet Slave Port</summary>
        public int Port { get; set; } = 5891;

        /// <summary>是否已連線</summary>
        public bool IsConnected => _client?.Connected == true;

        /// <summary>最新一筆力矩資料</summary>
        public JointTorqueSnapshot? LatestTorque { get; private set; }

        /// <summary>收到新力矩時觸發（背景執行緒，UI 需 Dispatcher）</summary>
        public event Action<JointTorqueSnapshot>? TorqueReceived;

        /// <summary>連線或接收錯誤時觸發</summary>
        public event Action<string>? ErrorOccurred;

        /// <summary>連線到機器人並開始接收</summary>
        public async Task ConnectAsync(string ip= "169.254.84.33",int Port=5891)
        {
            if (IsConnected)
                Disconnect(); // 確保連線前先清除舊資源

            _client = new TcpClient();
            await _client.ConnectAsync(ip, Port);
            _stream = _client.GetStream();

            _cts = new CancellationTokenSource();
           
           _= Task.Run(() => ReceiveLoop(_cts.Token)); //_ = 是 C# 的 Discard (捨棄) 語法。有意忽略這個 Task 物件，Run內使用匿名方法生成一Action
           
        }

        /// <summary>斷線並停止接收</summary>
        public void Disconnect()
        {
            _cts?.Cancel();
            _stream?.Close();
            _client?.Close();
            _stream = null;
            _client = null;
            _buffer.Clear();
        }

        /// <summary>背景接收迴圈</summary>
        private async Task ReceiveLoop(CancellationToken token)
        {
            var readBuffer = new byte[8192]; //8kB緩衝

            try
            {
                while (!token.IsCancellationRequested && _stream != null) //只要沒有收到取消請求，且網路串流 (_stream) 仍然存在，就一直執行迴圈。
                {
                    int bytesRead = await _stream.ReadAsync(readBuffer, token); //等待封包資料
                    if (bytesRead <= 0)
                    {
                        System.Diagnostics.Debug.WriteLine("[ReceiveLoop] 連線已關閉 (bytesRead <= 0)");
                        break;
                    }

                    string chunk = Encoding.UTF8.GetString(readBuffer, 0, bytesRead);
                    // ① 檢查：有沒有收到原始資料
                    //System.Diagnostics.Debug.WriteLine($"[Receive] 收到 {bytesRead} bytes");
                    //System.Diagnostics.Debug.WriteLine($"[Receive] 內容: {chunk}");
                    _buffer.Append(Encoding.UTF8.GetString(readBuffer, 0, bytesRead));
                    //Console.WriteLine("Get Data!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");

                    // 可能一次收到多個封包
                    while (TryExtractPacket(out string packet))
                    {
                        ProcessPacket(packet);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常斷線
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
        }

        /// <summary>從緩衝區取出一筆完整 $TMSVR 封包</summary>
        private bool TryExtractPacket(out string packet)
        {
            packet = string.Empty;
            string text = _buffer.ToString();
            int endIndex = text.IndexOf("\r\n", StringComparison.Ordinal); //查找符號
            if (endIndex < 0) return false;

            packet = text.Substring(0, endIndex);
            _buffer.Remove(0, endIndex + 2);
            return packet.StartsWith("$TMSVR", StringComparison.Ordinal);//檢查是否開頭為$TMSVR
        }

        /// <summary>解析封包並取 Joint_Torque</summary>
        private void ProcessPacket(string packet)
        {
            double[]? torques = ParseJointTorque(packet);
            if (torques == null) return;

            var snapshot = new JointTorqueSnapshot
            {
                Timestamp = DateTime.Now,
                J1 = torques.Length > 0 ? torques[0] : 0,
                J2 = torques.Length > 1 ? torques[1] : 0,
                J3 = torques.Length > 2 ? torques[2] : 0,
                J4 = torques.Length > 3 ? torques[3] : 0,
                J5 = torques.Length > 4 ? torques[4] : 0,
                J6 = torques.Length > 5 ? torques[5] : 0,
            };

            LatestTorque = snapshot;
            TorqueReceived?.Invoke(snapshot); //invoke內可以接需傳遞參數
        }

        /// <summary>從 $TMSVR 封包解析 Joint_Torque 陣列</summary>
        private double[]? ParseJointTorque(string packet)
        {
            try
            {
                int jsonStart = packet.IndexOf("[", StringComparison.Ordinal);
                int jsonEnd = packet.LastIndexOf("]", StringComparison.Ordinal);
                if (jsonStart < 0 || jsonEnd < 0) return null;

                string json = packet.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var items = JsonSerializer.Deserialize<List<SvrItem>>(json); //反序列化分配轉變為C#定義的數據型態 名稱要一樣
                if (items == null) return null;

                var torqueItem = items.FirstOrDefault(x => x.Item == "Joint_Torque_EST");
                if (torqueItem?.Value == null) return null;
                return torqueItem.Value.EnumerateArray()
                    .Select(x => x.GetDouble())
                    .ToArray();
                //return torqueItem.Value;
            }
            catch(JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JsonError] {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[JsonError] packet={packet}");
                ErrorOccurred?.Invoke($"JSON parse error: {ex.Message}");
                return null;

                
            }
        }

        public void Dispose() => Disconnect();

        // --- 內部用於 JSON 反序列化 ---
        private class SvrItem
        {
            public string Item { get; set; } = "";
            public JsonElement Value { get; set; }
        }
    }

    /// <summary>一筆力矩快照</summary>
    public class JointTorqueSnapshot
    {
        public DateTime Timestamp { get; set; }
        public double J1 { get; set; }
        public double J2 { get; set; }
        public double J3 { get; set; }
        public double J4 { get; set; }
        public double J5 { get; set; }
        public double J6 { get; set; }
        

        public double[] ToArray() => new[] { J1, J2, J3, J4, J5, J6 };

        public double Max_min(out double min )
        {
            double max = 50.0;

            min = 1;
            return max;

        }
    }
}
