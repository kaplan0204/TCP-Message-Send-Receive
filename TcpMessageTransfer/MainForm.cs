using System;
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
        private const int HeaderSize = 5;
        private const long MaxFileSize = 512L * 1024 * 1024;

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
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private string saveFolder;
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource cancellation;

        public MainForm()
        {
            Text = "TCP Mesaj ve Dosya Transferi (.NET Framework 4.8)";
            Width = 820; Height = 560; MinimumSize = new System.Drawing.Size(700, 450);
            saveFolder = Path.Combine(Application.StartupPath, "AlinanDosyalar");
            Directory.CreateDirectory(saveFolder);
            BuildUi();
            FormClosing += async (s, e) => await DisconnectAsync();
        }

        private void BuildUi()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8), WrapContents = true };
            top.Controls.Add(new Label { Text = "IP / Host:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) });
            top.Controls.Add(hostBox);
            top.Controls.Add(new Label { Text = "Port:", AutoSize = true, Margin = new Padding(10, 8, 3, 3) });
            top.Controls.Add(portBox); top.Controls.Add(startButton); top.Controls.Add(connectButton); top.Controls.Add(statusLabel);
            startButton.Click += async (s, e) => await StartServerAsync();
            connectButton.Click += async (s, e) => await ConnectAsync();

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8) };
            bottom.Controls.Add(messageBox); bottom.Controls.Add(sendMessageButton); bottom.Controls.Add(sendFileButton); bottom.Controls.Add(folderButton);
            sendMessageButton.Click += async (s, e) => await SendMessageAsync();
            sendFileButton.Click += async (s, e) => await SendFileAsync();
            folderButton.Click += ChooseFolder;
            Controls.Add(logBox); Controls.Add(bottom); Controls.Add(top);
            Log("Hazır. Bir pencerede sunucuyu başlatın, diğerinde bağlanın.");
        }

        private async Task StartServerAsync()
        {
            try
            {
                await DisconnectAsync();
                cancellation = new CancellationTokenSource();
                listener = new TcpListener(IPAddress.Any, (int)portBox.Value);
                listener.Start();
                Log("Sunucu başladı. Port: " + portBox.Value);
                SetStatus("Bağlantı bekleniyor...");
                client = await listener.AcceptTcpClientAsync();
                stream = client.GetStream();
                SetStatus("Bağlı"); Log("Karşı taraf bağlandı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Sunucu hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async Task ConnectAsync()
        {
            try
            {
                await DisconnectAsync();
                cancellation = new CancellationTokenSource();
                client = new TcpClient();
                await client.ConnectAsync(hostBox.Text.Trim(), (int)portBox.Value);
                stream = client.GetStream(); SetStatus("Bağlı");
                Log("Bağlanıldı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (Exception ex) { Log("Bağlantı hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async Task ReceiveLoopAsync(TcpClient tcp, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] header = await ReadExactlyAsync(tcp.GetStream(), HeaderSize, token);
                    byte type = header[0]; int length = ReadInt32(header, 1);
                    if (length < 0 || length > int.MaxValue - HeaderSize) throw new InvalidDataException("Geçersiz paket boyutu.");
                    if (type == MessagePacket)
                        Log("Karşı taraf: " + Encoding.UTF8.GetString(await ReadExactlyAsync(tcp.GetStream(), length, token)));
                    else if (type == FilePacket)
                        await ReceiveFileAsync(tcp.GetStream(), length, token);
                    else throw new InvalidDataException("Bilinmeyen paket türü.");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Bağlantı kapandı: " + ex.Message); SetStatus("Bağlı değil"); }
        }

        private async Task ReceiveFileAsync(NetworkStream input, int payloadLength, CancellationToken token)
        {
            byte[] nameLengthBytes = await ReadExactlyAsync(input, 4, token);
            int nameLength = ReadInt32(nameLengthBytes, 0);
            if (nameLength < 1 || nameLength > 1024 || nameLength + 4 > payloadLength) throw new InvalidDataException("Dosya adı geçersiz.");
            string name = Path.GetFileName(Encoding.UTF8.GetString(await ReadExactlyAsync(input, nameLength, token)));
            long contentLength = payloadLength - 4L - nameLength;
            if (contentLength < 0 || contentLength > MaxFileSize) throw new InvalidDataException("Dosya boyutu geçersiz.");
            string path = GetUniquePath(name);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await CopyExactlyAsync(input, output, contentLength, token);
            Log("Dosya alındı: " + path);
        }

        private async Task SendMessageAsync()
        {
            string text = messageBox.Text.Trim(); if (text.Length == 0) return;
            try { await SendPacketAsync(MessagePacket, Encoding.UTF8.GetBytes(text), cancellation.Token); Log("Ben: " + text); messageBox.Clear(); }
            catch (Exception ex) { Log("Mesaj gönderilemedi: " + ex.Message); }
        }

        private async Task SendFileAsync()
        {
            if (stream == null) { Log("Önce bağlantı kurun."); return; }
            using (var dialog = new OpenFileDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                var info = new FileInfo(dialog.FileName);
                byte[] name = Encoding.UTF8.GetBytes(info.Name);
                long payloadLength = 4L + name.Length + info.Length;
                if (info.Length > MaxFileSize || payloadLength > int.MaxValue) { Log("Dosya çok büyük (en fazla 512 MB)."); return; }
                try
                {
                    await sendLock.WaitAsync(cancellation.Token);
                    try
                    {
                        byte[] header = new byte[HeaderSize]; header[0] = FilePacket; WriteInt32(header, 1, (int)payloadLength);
                        await stream.WriteAsync(header, 0, header.Length, cancellation.Token);
                        byte[] nameLength = new byte[4]; WriteInt32(nameLength, 0, name.Length);
                        await stream.WriteAsync(nameLength, 0, 4, cancellation.Token);
                        await stream.WriteAsync(name, 0, name.Length, cancellation.Token);
                        using (var input = new FileStream(dialog.FileName, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                            await input.CopyToAsync(stream, 81920, cancellation.Token);
                        await stream.FlushAsync(cancellation.Token);
                    }
                    finally { sendLock.Release(); }
                    Log("Dosya gönderildi: " + info.Name);
                }
                catch (Exception ex) { Log("Dosya gönderilemedi: " + ex.Message); }
            }
        }

        private async Task SendPacketAsync(byte type, byte[] payload, CancellationToken token)
        {
            if (stream == null) throw new InvalidOperationException("Bağlantı yok.");
            byte[] header = new byte[HeaderSize]; header[0] = type; WriteInt32(header, 1, payload.Length);
            await sendLock.WaitAsync(token);
            try { await stream.WriteAsync(header, 0, header.Length, token); await stream.WriteAsync(payload, 0, payload.Length, token); await stream.FlushAsync(token); }
            finally { sendLock.Release(); }
        }

        private async Task DisconnectAsync()
        {
            try { cancellation?.Cancel(); listener?.Stop(); if (stream != null) await stream.FlushAsync(); stream?.Close(); client?.Close(); }
            catch { }
            finally { stream = null; client = null; listener = null; cancellation?.Dispose(); cancellation = null; SetStatus("Bağlı değil"); }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream input, int count, CancellationToken token)
        {
            byte[] data = new byte[count]; int offset = 0;
            while (offset < count) { int read = await input.ReadAsync(data, offset, count - offset, token); if (read == 0) throw new EndOfStreamException(); offset += read; }
            return data;
        }

        private static async Task CopyExactlyAsync(Stream input, Stream output, long count, CancellationToken token)
        {
            byte[] buffer = new byte[81920]; long remaining = count;
            while (remaining > 0) { int wanted = (int)Math.Min(buffer.Length, remaining); int read = await input.ReadAsync(buffer, 0, wanted, token); if (read == 0) throw new EndOfStreamException(); await output.WriteAsync(buffer, 0, read, token); remaining -= read; }
        }

        private string GetUniquePath(string name)
        {
            string path = Path.Combine(saveFolder, name); int i = 1;
            while (File.Exists(path)) { path = Path.Combine(saveFolder, Path.GetFileNameWithoutExtension(name) + "_" + i++ + Path.GetExtension(name)); }
            return path;
        }

        private static int ReadInt32(byte[] b, int i) { return (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3]; }
        private static void WriteInt32(byte[] b, int i, int v) { b[i] = (byte)(v >> 24); b[i + 1] = (byte)(v >> 16); b[i + 2] = (byte)(v >> 8); b[i + 3] = (byte)v; }
        private void ChooseFolder(object sender, EventArgs e) { using (var d = new FolderBrowserDialog { SelectedPath = saveFolder }) if (d.ShowDialog() == DialogResult.OK) { saveFolder = d.SelectedPath; Log("Kayıt klasörü: " + saveFolder); } }
        private void SetStatus(string text) { if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), text); return; } statusLabel.Text = text; }
        private void Log(string text) { if (IsDisposed) return; if (InvokeRequired) { BeginInvoke(new Action<string>(Log), text); return; } logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine); }
    }
}
