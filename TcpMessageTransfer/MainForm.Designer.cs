namespace TcpMessageTransfer
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.TextBox hostBox;
        private System.Windows.Forms.NumericUpDown portBox;
        private System.Windows.Forms.Button startButton;
        private System.Windows.Forms.Button connectButton;
        private System.Windows.Forms.Button sendMessageButton;
        private System.Windows.Forms.Button sendFileButton;
        private System.Windows.Forms.Button folderButton;
        private System.Windows.Forms.TextBox messageBox;
        private System.Windows.Forms.TextBox logBox;
        private System.Windows.Forms.Label statusLabel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            hostBox = new System.Windows.Forms.TextBox();
            portBox = new System.Windows.Forms.NumericUpDown();
            startButton = new System.Windows.Forms.Button();
            connectButton = new System.Windows.Forms.Button();
            sendMessageButton = new System.Windows.Forms.Button();
            sendFileButton = new System.Windows.Forms.Button();
            folderButton = new System.Windows.Forms.Button();
            messageBox = new System.Windows.Forms.TextBox();
            logBox = new System.Windows.Forms.TextBox();
            statusLabel = new System.Windows.Forms.Label();
            var topPanel = new System.Windows.Forms.FlowLayoutPanel();
            var bottomPanel = new System.Windows.Forms.FlowLayoutPanel();
            var hostLabel = new System.Windows.Forms.Label();
            var portLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(portBox)).BeginInit();
            SuspendLayout();

            topPanel.Dock = System.Windows.Forms.DockStyle.Top;
            topPanel.Height = 76;
            topPanel.Padding = new System.Windows.Forms.Padding(8);
            topPanel.WrapContents = true;
            topPanel.Controls.Add(hostLabel);
            topPanel.Controls.Add(hostBox);
            topPanel.Controls.Add(portLabel);
            topPanel.Controls.Add(portBox);
            topPanel.Controls.Add(startButton);
            topPanel.Controls.Add(connectButton);
            topPanel.Controls.Add(statusLabel);

            hostLabel.AutoSize = true;
            hostLabel.Text = "IP / Host:";
            hostLabel.Margin = new System.Windows.Forms.Padding(3, 8, 3, 3);
            hostBox.Width = 120;
            hostBox.Text = "127.0.0.1";

            portLabel.AutoSize = true;
            portLabel.Text = "Port:";
            portLabel.Margin = new System.Windows.Forms.Padding(10, 8, 3, 3);
            portBox.Minimum = 1;
            portBox.Maximum = 65535;
            portBox.Value = 5000;
            portBox.Width = 70;

            startButton.AutoSize = true;
            startButton.Text = "Sunucuyu Başlat";
            startButton.Click += StartButton_Click;
            connectButton.AutoSize = true;
            connectButton.Text = "Bağlan";
            connectButton.Click += ConnectButton_Click;
            statusLabel.AutoSize = true;
            statusLabel.Text = "Bağlı değil";

            bottomPanel.Dock = System.Windows.Forms.DockStyle.Bottom;
            bottomPanel.Height = 48;
            bottomPanel.Padding = new System.Windows.Forms.Padding(8);
            bottomPanel.Controls.Add(messageBox);
            bottomPanel.Controls.Add(sendMessageButton);
            bottomPanel.Controls.Add(sendFileButton);
            bottomPanel.Controls.Add(folderButton);

            messageBox.Width = 420;
            sendMessageButton.AutoSize = true;
            sendMessageButton.Text = "Mesaj Gönder";
            sendMessageButton.Click += SendMessageButton_Click;
            sendFileButton.AutoSize = true;
            sendFileButton.Text = "Dosya Gönder";
            sendFileButton.Click += SendFileButton_Click;
            folderButton.AutoSize = true;
            folderButton.Text = "Klasör Seç";
            folderButton.Click += FolderButton_Click;

            logBox.Dock = System.Windows.Forms.DockStyle.Fill;
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;

            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(820, 560);
            MinimumSize = new System.Drawing.Size(700, 450);
            Text = "TCP Mesaj ve Dosya Transferi (.NET Framework 4.8)";
            Controls.Add(logBox);
            Controls.Add(bottomPanel);
            Controls.Add(topPanel);
            FormClosing += MainForm_FormClosing;
            ((System.ComponentModel.ISupportInitialize)(portBox)).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
