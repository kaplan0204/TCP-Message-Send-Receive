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
    public sealed partial class MainForm : Form
    {
        private const byte MessagePacket = 1;
        private const byte FilePacket = 2;
        private const int HeaderSize = 5;
        private const long MaxFileSize = 512L * 1024 * 1024;

        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private string saveFolder;
        private TcpListener listener;
        private TcpClient client;
        private NetworkStream stream;
        private CancellationTokenSource cancellation;

        public MainForm()
        {
            InitializeComponent();
            saveFolder = Path.Combine(Application.StartupPath, "AlinanDosyalar");
            Directory.CreateDirectory(saveFolder);
            Log("Hazır. Bir pencerede sunucuyu başlatın, diğerinde bağlanın.");
        }

        private async void StartButton_Click(object sender, EventArgs e)
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
                SetStatus("Bağlı");
                Log("Karşı taraf bağlandı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log("Sunucu hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async void ConnectButton_Click(object sender, EventArgs e)
        {
            try
            {
                await DisconnectAsync();
                cancellation = new CancellationTokenSource();
                client = new TcpClient();
                await client.ConnectAsync(hostBox.Text.Trim(), (int)portBox.Value);
                stream = client.GetStream();
                SetStatus("Bağlı");
                Log("Bağlanıldı: " + client.Client.RemoteEndPoint);
                _ = ReceiveLoopAsync(client, cancellation.Token);
            }
            catch (Exception ex) { Log("Bağlantı hatası: " + ex.Message); SetStatus("Hata"); }
        }

        private async void SendMessageButton_Click(object sender, EventArgs e)
        {
            string text = messageBox.Text.Trim();
            if (text.Length == 0) return;
            try
            {
                if (cancellation == null) throw new InvalidOperationException("Bağlantı yok.");
                await SendPacketAsync(MessagePacket, Encoding.UTF8.GetBytes(text), cancellation.Token);
                Log("Ben: " + text);
                messageBox.Clear();
            }
            catch (Exception ex) { Log("Mesaj gönderilemedi: " + ex.Message); }
        }

        private async void SendFileButton_Click(object sender, EventArgs e)
        {
            if (stream == null || cancellation == null) { Log("Önce bağlantı kurun."); return; }
            using (var dialog = new OpenFileDialog())
            {
                if (dialog.ShowDialog() != DialogResult.OK) return;
                var info = new FileInfo(dialog.FileName);
                byte[] name = Encoding.UTF8.GetBytes(info.Name);
                long payloadLength = 4L + name.Length + info.Length;
                if (info.Length > MaxFileSize || payloadLength > int.MaxValue)
                {
                    Log("Dosya çok büyük (en fazla 512 MB).");
                    return;
                }

                try
                {
                    await sendLock.WaitAsync(cancellation.Token);
                    try
                    {
                        byte[] header = new byte[HeaderSize];
                        header[0] = FilePacket;
                        WriteInt32(header, 1, (int)payloadLength);
                        await stream.WriteAsync(header, 0, header.Length, cancellation.Token);

                        byte[] nameLength = new byte[4];
                        WriteInt32(nameLength, 0, name.Length);
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

        private async Task ReceiveLoopAsync(TcpClient tcp, CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    byte[] header = await ReadExactlyAsync(tcp.GetStream(), HeaderSize, token);
                    byte type = header[0];
                    int length = ReadInt32(header, 1);
                    if (length < 0 || length > int.MaxValue - HeaderSize)
                        throw new InvalidDataException("Geçersiz paket boyutu.");

                    if (type == MessagePacket)
                    {
                        byte[] payload = await ReadExactlyAsync(tcp.GetStream(), length, token);
                        Log("Karşı taraf: " + Encoding.UTF8.GetString(payload));
                    }
                    else if (type == FilePacket)
                    {
                        await ReceiveFileAsync(tcp.GetStream(), length, token);
                    }
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
            if (nameLength < 1 || nameLength > 1024 || nameLength + 4 > payloadLength)
                throw new InvalidDataException("Dosya adı geçersiz.");

            string name = Path.GetFileName(Encoding.UTF8.GetString(await ReadExactlyAsync(input, nameLength, token)));
            long contentLength = payloadLength - 4L - nameLength;
            if (contentLength < 0 || contentLength > MaxFileSize)
                throw new InvalidDataException("Dosya boyutu geçersiz.");

            string path = GetUniquePath(name);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await CopyExactlyAsync(input, output, contentLength, token);
            Log("Dosya alındı: " + path);
        }

        private async Task SendPacketAsync(byte type, byte[] payload, CancellationToken token)
        {
            if (stream == null) throw new InvalidOperationException("Bağlantı yok.");
            byte[] header = new byte[HeaderSize];
            header[0] = type;
            WriteInt32(header, 1, payload.Length);
            await sendLock.WaitAsync(token);
            try
            {
                await stream.WriteAsync(header, 0, header.Length, token);
                await stream.WriteAsync(payload, 0, payload.Length, token);
                await stream.FlushAsync(token);
            }
            finally { sendLock.Release(); }
        }

        private async Task DisconnectAsync()
        {
            try
            {
                cancellation?.Cancel();
                listener?.Stop();
                if (stream != null) await stream.FlushAsync();
                stream?.Close();
                client?.Close();
            }
            catch { }
            finally
            {
                stream = null;
                client = null;
                listener = null;
                cancellation?.Dispose();
                cancellation = null;
                SetStatus("Bağlı değil");
            }
        }

        private async void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            await DisconnectAsync();
            sendLock.Dispose();
        }

        private void FolderButton_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = saveFolder })
            {
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    saveFolder = dialog.SelectedPath;
                    Log("Kayıt klasörü: " + saveFolder);
                }
            }
        }

        private static async Task<byte[]> ReadExactlyAsync(NetworkStream input, int count, CancellationToken token)
        {
            byte[] data = new byte[count];
            int offset = 0;
            while (offset < count)
            {
                int read = await input.ReadAsync(data, offset, count - offset, token);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
            return data;
        }

        private static async Task CopyExactlyAsync(Stream input, Stream output, long count, CancellationToken token)
        {
            byte[] buffer = new byte[81920];
            long remaining = count;
            while (remaining > 0)
            {
                int wanted = (int)Math.Min(buffer.Length, remaining);
                int read = await input.ReadAsync(buffer, 0, wanted, token);
                if (read == 0) throw new EndOfStreamException();
                await output.WriteAsync(buffer, 0, read, token);
                remaining -= read;
            }
        }

        private string GetUniquePath(string name)
        {
            string path = Path.Combine(saveFolder, name);
            int index = 1;
            while (File.Exists(path))
                path = Path.Combine(saveFolder, Path.GetFileNameWithoutExtension(name) + "_" + index++ + Path.GetExtension(name));
            return path;
        }

        private static int ReadInt32(byte[] bytes, int index)
        {
            return (bytes[index] << 24) | (bytes[index + 1] << 16) | (bytes[index + 2] << 8) | bytes[index + 3];
        }

        private static void WriteInt32(byte[] bytes, int index, int value)
        {
            bytes[index] = (byte)(value >> 24);
            bytes[index + 1] = (byte)(value >> 16);
            bytes[index + 2] = (byte)(value >> 8);
            bytes[index + 3] = (byte)value;
        }

        private void SetStatus(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(SetStatus), text); return; }
            statusLabel.Text = text;
        }

        private void Log(string text)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), text); return; }
            logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
        }
    }
}
