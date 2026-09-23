using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TcpMessageTransfer
{
    public sealed class MainForm : Form
    {
        private const byte MessagePacket = 1;
        private const byte FilePacket = 2;
        private const int MaxPacketSize = 512 * 1024 * 1024;

        private readonly TextBox hostBox = new TextBox { Text = "127.0.0.1", Width = 120 };
        private readonly NumericUpDown portBox = new NumericUpDown { Minimum = 1, Maximum = 65535, Value = 5000, Width = 70 };
        private readonly Button startButton = new Button { Text = "Sunucuyu Başlat", AutoSize = true };
        private readonly Button connectButton = new Button { Text = "Bağlan", AutoSize = true };
        private readonly Button sendMessageButton = new Button { Text = "Mesaj Gönder", AutoSize = true };
        private readonly Button sendFileButton = new Button { Text = "Dosya Gönder", AutoSize = true };
        private readonly Button folderButton = new Button { Text = "Klasör Seç", AutoSize = true };
        private readonly TextBox messageBox = new TextBox { Width = 420 };
        private readonly TextBox logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly Label statusLabel = new Label { Text = "Bağlı değil", AutoSize = true };
        private string saveFolder;
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource cancellation;
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

        public MainForm()
        {
            Text = "TCP Mesaj ve Dosya Transferi (.NET Framework 4.8)";
            Width = 820; Height = 560; MinimumSize = new System.Drawing.Size(700, 450);
            saveFolder = Path.Combine(Application.StartupPath, "AlinanDosyalar");
            Directory.CreateDirectory(saveFolder);
            BuildUi();
            FormClosing += (s, e) => Disconnect();
        }

        private void BuildUi()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8), WrapContents = true };
            top.Controls.Add(new Label { Text = "IP / Host:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            top.Controls.Add(hostBox);
            top.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) });
            top.Controls.Add(portBox); top.Controls.Add(startButton); top.Controls.Add(connectButton);
            top.Controls.Add(statusLabel);
            startButton.Click += async (s, e) => await StartServerAsync();
            connectButton.Click += async (s, e) => await ConnectAsync();

            var send = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
            send.Controls.Add(messageBox); send.Controls.Add(sendMessageButton); send.Controls.Add(sendFileButton); send.Controls.Add(folderButton);
            sendMessageButton.Click += async (s, e) => await SendMessageAsync();
            sendFileButton.Click += async (s, e) => await SendFileAsync();
            folderButton.Click += ChooseFolder;
            Controls.Add(logBox); Controls.Add(send); Controls.Add(top);
            Log("Hazır. Bir pencerede sunucuyu başlatın, diğerinde bağlanın.");
        }

        private async Task StartServerAsync()
        {
            try
            {
                Disconnect(); cancellation = new CancellationTokenSource();
                listener = new TcpListener(IPAddress.Any, (int)portBox.Value); listener.Start();
                Log("Sunucu başladı. Port: " + portBox.Value);
                SetStatus("Bağlantı bekleniyor...");
                client = await listener.AcceptTcpClientAsync();
                stream = client.GetStream(); SetStatus("Bağlı"); Log("Karşı taraf bağlandı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (Exception ex) { Log("Sunucu hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async Task ConnectAsync()
        {
            try
            {
                Disconnect(); cancellation = new CancellationTokenSource();
                client = new TcpClient(); await client.ConnectAsync(hostBox.Text.Trim(), (int)portBox.Value);
                stream = client.GetStream(); SetStatus("Bağlı"); Log("Bağlanıldı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (Exception ex) { Log("Bağlantı hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async Task ReceiveLoopAsync(TcpClient tcp, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && tcp.Connected)
                {
                    byte[] header = await ReadExactlyAsync(tcp.GetStream(), 5, token);
                    byte type = header[0]; int length = ReadInt32(header, 1);
                    if (length < 0 || length > MaxPacketSize) throw new InvalidDataException("Geçersiz paket boyutu.");
                    byte[] payload = await ReadExactlyAsync(tcp.GetStream(), length, token);
                    if (type == MessagePacket) Log("Karşı taraf: " + Encoding.UTF8.GetString(payload));
                    else if (type == FilePacket) ReceiveFile(payload);
                    else throw new InvalidDataException("Bilinmeyen paket türü.");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Bağlantı kapandı: " + ex.Message); SetStatus("Bağlı değil"); }
        }

        private void ReceiveFile(byte[] payload)
        {
            using (var ms = new MemoryStream(payload)) using (var br = new BinaryReader(ms, Encoding.UTF8))
            {
                int nameLength = br.ReadInt32(); if (nameLength < 1 || nameLength > 1024) throw new InvalidDataException("Dosya adı geçersiz.");
                string name = Path.GetFileName(Encoding.UTF8.GetString(br.ReadBytes(nameLength)));
                string path = Path.Combine(saveFolder, name);
                if (File.Exists(path)) path = Path.Combine(saveFolder, Path.GetFileNameWithoutExtension(name) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + Path.GetExtension(name));
                File.WriteAllBytes(path, br.ReadBytes((int)(ms.Length - ms.Position)));
                Log("Dosya alındı: " + path);
            }
        }

        private async Task SendMessageAsync()
        {
            string text = messageBox.Text.Trim(); if (text.Length == 0) return;
            try { await SendPacketAsync(MessagePacket, Encoding.UTF8.GetBytes(text)); Log("Ben: " + text); messageBox.Clear(); }
            catch (Exception ex) { Log("Mesaj gönderilemedi: " + ex.Message); }
        }

        private async Task SendFileAsync()
        {
            if (stream == null) { Log("Önce bağlantı kurun."); return; }
            using (var dialog = new OpenFileDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                FileInfo info = new FileInfo(dialog.FileName);
                if (info.Length > MaxPacketSize - 1024) { Log("Dosya çok büyük (en fazla 512 MB)."); return; }
                byte[] name = Encoding.UTF8.GetBytes(info.Name); byte[] data = File.ReadAllBytes(dialog.FileName);
                using (var ms = new MemoryStream()) using (var bw = new BinaryWriter(ms, Encoding.UTF8))
                { bw.Write(name.Length); bw.Write(name); bw.Write(data); await SendPacketAsync(FilePacket, ms.ToArray()); }
                Log("Dosya gönderildi: " + info.Name);
            }
        }

        private async Task SendPacketAsync(byte type, byte[] payload)
        {
            if (stream == null) throw new InvalidOperationException("Bağlantı yok.");
            byte[] header = new byte[5]; header[0] = type; WriteInt32(header, 1, payload.Length);
            await sendLock.WaitAsync(); try { await stream.WriteAsync(header, 0, header.Length); await stream.WriteAsync(payload, 0, payload.Length); await stream.FlushAsync(); } finally { sendLock.Release(); }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream s, int count, CancellationToken token)
        {
            byte[] result = new byte[count]; int offset = 0;
            while (offset < count) { int n = await s.ReadAsync(result, offset, count - offset, token); if (n == 0) throw new EndOfStreamException(); offset += n; }
            return result;
        }
        private static int ReadInt32(byte[] b, int i) { return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; }
        private static void WriteInt32(byte[] b, int i, int v) { b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v; }
        private void ChooseFolder(object sender, EventArgs e) { using (var d = new FolderBrowserDialog { SelectedPath = saveFolder }) if (d.ShowDialog() == DialogResult.OK) { saveFolder = d.SelectedPath; Log("Kayıt klasörü: " + saveFolder); } }
        private void Disconnect() { try { cancellation?.Cancel(); stream?.Close(); client?.Close(); listener?.Stop(); } catch { } stream = null; client = null; listener = null; SetStatus("Bağlı değil"); }
        private void SetStatus(string text) { if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), text); return; } statusLabel.Text = text; }
        private void Log(string text) { if (InvokeRequired) { BeginInvoke(new Action<string>(Log), text); return; } logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine); }
    }
}
