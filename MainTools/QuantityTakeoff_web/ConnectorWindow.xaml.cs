// ConnectorWindow.xaml.cs
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using System;
using System.Diagnostics; // Process 클래스 사용을 위해 추가
using System.Threading.Tasks;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace RevitDjangoConnector
{
    public partial class ConnectorWindow : Window
    {
        private readonly ExternalEvent _externalEvent;
        private readonly RevitApiHandler _apiHandler;
        private WebSocketService _webSocketService;

        public ConnectorWindow(ExternalEvent externalEvent, RevitApiHandler apiHandler)
        {
            InitializeComponent();
            _externalEvent = externalEvent;
            _apiHandler = apiHandler;
        }
        public void UpdateProgress(int current, int total)
        {
            Dispatcher.Invoke(() =>
            {
                if (total > 0 && current >= 0)
                {
                    ProgressBar.Maximum = total;
                    ProgressBar.Value = current;
                    ProgressTextBlock.Text = $"{current} / {total} ({((double)current / total * 100):F0}%)";
                }
            });
        }

        public void ResetProgress(string initialMessage = "Ready")
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Value = 0;
                ProgressTextBlock.Text = initialMessage;
            });
        }
        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            var serverUrl = ServerUrlTextBox.Text;
            if (string.IsNullOrWhiteSpace(serverUrl))
            {
                MessageBox.Show("Please enter a server URL.");
                return;
            }

            _webSocketService = new WebSocketService(serverUrl);
            _webSocketService.OnMessageReceived += HandleServerMessage;
            _apiHandler.Setup(_webSocketService, UpdateStatus, UpdateProgress, ResetProgress);

            try
            {
                await _webSocketService.ConnectAsync();
                if (_webSocketService.IsConnected)
                {
                    UpdateStatus("Connected to server.");
                    ConnectButton.IsEnabled = false;
                    DisconnectButton.IsEnabled = true;

                    try
                    {
                        string httpUrl = serverUrl.Replace("ws://", "http://").Split(new[] { "/ws/" }, StringSplitOptions.None)[0];

                        // ▼▼▼ [수정] 웹 브라우저를 여는 코드를 안정적인 방식으로 변경합니다. ▼▼▼
                        var psi = new ProcessStartInfo
                        {
                            FileName = httpUrl,
                            UseShellExecute = true
                        };
                        Process.Start(psi);
                    }
                    catch (Exception ex)
                    {
                        UpdateStatus($"Could not open browser: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatus($"Failed to connect: {ex.Message}");
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webSocketService != null)
            {
                await _webSocketService.DisconnectAsync();
                _webSocketService = null;
            }
            UpdateStatus("Disconnected.");
            ConnectButton.IsEnabled = true;
            DisconnectButton.IsEnabled = false;
            ResetProgress(); 

        }

        private void HandleServerMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var jsonMessage = JObject.Parse(message);

                    if (jsonMessage.Value<string>("command") == "disconnected_by_server")
                    {
                        UpdateStatus("Disconnected by server. Please reconnect.");
                        ConnectButton.IsEnabled = true;
                        DisconnectButton.IsEnabled = false;
                        return;
                    }

                    UpdateStatus($"Command received: {jsonMessage.Value<string>("command")}");
                    _apiHandler.LastCommandData = jsonMessage;
                    _externalEvent.Raise();
                }
                catch (Exception ex)
                {
                    UpdateStatus($"Error processing message: {ex.Message}");
                }
            });
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                StatusTextBox.ScrollToEnd();
            });
        }

        private async void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_webSocketService != null && _webSocketService.IsConnected)
            {
                await _webSocketService.DisconnectAsync();
            }
        }
    }
}