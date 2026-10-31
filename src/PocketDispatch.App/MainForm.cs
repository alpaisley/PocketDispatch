using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using PocketDispatch.Core;

namespace PocketDispatch.App
{
    /// <summary>Main window: flight card, fetch button, ACARS log. Lives in the tray.</summary>
    public sealed class MainForm : Form
    {
        readonly AppSettings _s = AppSettings.Load();
        readonly SimBriefService _svc = new SimBriefService();
        readonly System.Windows.Forms.Timer _poll = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        readonly NotifyIcon _tray = new NotifyIcon();
        readonly HeaderBar _header = new HeaderBar();
        readonly Pill _pill = new Pill();
        readonly FlightCard _card = new FlightCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12) };
        readonly RichTextBox _log = new RichTextBox();
        readonly Label _footer = new Label();
        readonly Button _fetch = Theme.Button("FETCH FLIGHT PLAN", true);
        string _lastRequestId;
        string _lastBrief = "";
        bool _busy;
        int _countdown;

        public MainForm()
        {
            Text = "Pocket Dispatch";
            ClientSize = new Size(980, 620);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.5f);
            Icon = Theme.MakeIcon();
            DoubleBuffered = true;

            // ---- header + pill
            _pill.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _header.Controls.Add(_pill);
            _header.Resize += (s, e) => _pill.Location = new Point(_header.Width - _pill.Width - 18, 19);
            _pill.On = _s.AutoFetch;
            _pill.Toggled += (s, e) => { _s.AutoFetch = _pill.On; _s.Save(); UpdatePolling(); };

            // ---- left column: card + buttons
            var openBrief = Theme.Button("Open brief", false);
            var openAvitab = Theme.Button("AviTab folder", false);
            var settings = Theme.Button("Settings", false);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0), BackColor = Theme.Bg };
            buttons.Controls.Add(_fetch);
            foreach (var b in new[] { openBrief, openAvitab, settings })
            {
                b.Margin = new Padding(0, 6, 8, 8);
                buttons.Controls.Add(b);
            }

            // ---- right column: ACARS log
            var acarsTitle = new Label
            {
                Text = "ACARS", Dock = DockStyle.Top, Height = 30, ForeColor = Theme.Cyan,
                Font = Theme.Din(11f, FontStyle.Bold), Padding = new Padding(14, 8, 0, 0), BackColor = Theme.BgDeep,
            };
            _log.Dock = DockStyle.Fill;
            _log.ReadOnly = true;
            _log.BorderStyle = BorderStyle.None;
            _log.BackColor = Theme.BgDeep;
            _log.ForeColor = Theme.Text;
            _log.Font = Theme.Mono(9.5f);
            _log.DetectUrls = false;
            _log.ScrollBars = RichTextBoxScrollBars.Vertical;
            var logWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 4, 8, 10), BackColor = Theme.BgDeep };
            logWrap.Controls.Add(_log);
            var acars = new Panel { Dock = DockStyle.Fill, BackColor = Theme.BgDeep, Margin = new Padding(16, 0, 0, 0) };
            acars.Controls.Add(logWrap);
            acars.Controls.Add(acarsTitle);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(18, 18, 18, 6), BackColor = Theme.Bg };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            body.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            body.Controls.Add(_card, 0, 0);
            body.Controls.Add(buttons, 0, 1);
            body.Controls.Add(acars, 1, 0);
            body.SetRowSpan(acars, 2);

            _footer.Dock = DockStyle.Bottom;
            _footer.Height = 26;
            _footer.ForeColor = Theme.Muted;
            _footer.BackColor = Theme.BgDeep;
            _footer.Padding = new Padding(18, 5, 18, 0);
            _footer.Font = Theme.UI(8.5f);

            Controls.Add(body);
            Controls.Add(_footer);
            Controls.Add(_header);

            // ---- events
            _fetch.Click += async (s, e) => await FetchAsync(true);
            openBrief.Click += (s, e) =>
            {
                if (File.Exists(_lastBrief)) OpenPath(_lastBrief);
                else Say("No brief downloaded yet this session.", Theme.Muted);
            };
            openAvitab.Click += (s, e) => OpenPath(_s.ResolvedAviTab);
            settings.Click += (s, e) => ShowSettings();
            _svc.Log += m => Say(m.Trim(), m.TrimStart().StartsWith("!") ? Theme.Red : m.Contains("->") ? Theme.Green : Theme.Text);
            _poll.Tick += async (s, e) => await FetchAsync(false);
            _clock.Tick += (s, e) => { if (_countdown > 0) _countdown--; UpdateFooter(); };

            // ---- tray
            _tray.Icon = Icon;
            _tray.Text = "Pocket Dispatch";
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => RestoreFromTray();
            var menu = new ContextMenuStrip();
            menu.Items.Add("Fetch flight plan", null, async (s, e) => await FetchAsync(true));
            menu.Items.Add("Show Pocket Dispatch", null, (s, e) => RestoreFromTray());
            menu.Items.Add("Exit", null, (s, e) => { _tray.Visible = false; Application.Exit(); });
            _tray.ContextMenuStrip = menu;

            Resize += (s, e) => { if (WindowState == FormWindowState.Minimized) Hide(); };
            HandleCreated += (s, e) => Theme.DarkTitleBar(this);
            FormClosing += (s, e) => _tray.Visible = false;
            Shown += async (s, e) =>
            {
                Say("Pocket Dispatch ready.", Theme.Cyan);
                _card.Display(null, "", Theme.Muted);
                if (_s.StartMinimised) WindowState = FormWindowState.Minimized;
                if (!_s.IsConfigured)
                {
                    Say("Set your SimBrief Pilot ID and X-Plane folder in Settings.", Theme.Amber);
                    ShowSettings();
                }
                else await PrimeAsync();
                UpdatePolling();
                _clock.Start();
            };
        }

        void ShowSettings()
        {
            using (var f = new SettingsForm(_s))
            {
                if (f.ShowDialog(this) == DialogResult.OK)
                {
                    Say("Settings saved.", Theme.Muted);
                    UpdatePolling();
                    if (_lastRequestId == null && _s.IsConfigured) { var t = PrimeAsync(); }
                }
            }
            UpdateFooter();
        }

        void UpdatePolling()
        {
            _poll.Interval = Math.Max(15, _s.WatchSeconds) * 1000;
            _poll.Enabled = _s.AutoFetch && _s.IsConfigured;
            _countdown = _poll.Enabled ? _poll.Interval / 1000 : 0;
            _pill.Detail = "checking SimBrief every " + (_poll.Interval / 1000) + "s";
            _pill.Invalidate();
            UpdateFooter();
        }

        void UpdateFooter()
        {
            var who = !string.IsNullOrWhiteSpace(_s.PilotId) ? "Pilot ID " + _s.PilotId
                    : !string.IsNullOrWhiteSpace(_s.Username) ? _s.Username : "not set up";
            var next = _poll.Enabled ? "   ·   next check in " + _countdown + "s" : "";
            _footer.Text = who + "   ·   AviTab: " + Short(_s.ResolvedAviTab) + "   ·   FMS: " + Short(_s.ResolvedFms) + next;
        }

        static string Short(string p)
        {
            if (string.IsNullOrEmpty(p)) return "-";
            return p.Length <= 42 ? p : "..." + p.Substring(p.Length - 40);
        }

        /// <summary>Show what's currently on SimBrief, so auto-download only reacts to new plans.</summary>
        async Task PrimeAsync()
        {
            try
            {
                var o = await _svc.FetchLatestAsync(_s);
                _lastRequestId = o.RequestId;
                _card.Display(o, "ON SIMBRIEF  " + o.Generated.ToString("HH:mm") + "Z", Theme.Cyan);
                Say("Linked to SimBrief. Latest plan " + o.Get("callsign") + " " + o.Origin + "-" + o.Destination +
                    ". Hit FETCH to download it.", Theme.Text);
            }
            catch (Exception ex)
            {
                _card.SetStatus("OFFLINE", Theme.Red);
                Say("! " + ex.Message, Theme.Red);
            }
        }

        async Task FetchAsync(bool manual)
        {
            if (_busy) return;
            _countdown = _poll.Enabled ? _poll.Interval / 1000 : 0;
            if (!_s.IsConfigured)
            {
                Say("! Set your Pilot ID and X-Plane folder in Settings first.", Theme.Red);
                return;
            }
            _busy = true;
            _fetch.Enabled = false;
            _fetch.Text = "FETCHING...";
            try
            {
                var o = await _svc.FetchLatestAsync(_s);
                if (!manual && o.RequestId == _lastRequestId) return;
                _lastRequestId = o.RequestId;
                _card.Display(o, "DOWNLOADING", Theme.Amber);
                _lastBrief = await _svc.ProcessAsync(o, _s);
                _card.SetStatus("DOWNLOADED  " + DateTime.Now.ToString("HH:mm"), Theme.Green);
                if (!Visible || WindowState == FormWindowState.Minimized || !manual)
                    _tray.ShowBalloonTip(4000, "Pocket Dispatch",
                        o.Get("callsign") + "  " + o.Origin + " - " + o.Destination + "\nBrief in AviTab, flight plan in X-Plane.", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                _card.SetStatus("ERROR", Theme.Red);
                Say("! " + ex.Message, Theme.Red);
            }
            finally
            {
                _busy = false;
                _fetch.Enabled = true;
                _fetch.Text = "FETCH FLIGHT PLAN";
            }
        }

        void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        void Say(string msg, Color color)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => Say(msg, color))); return; }
            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = Theme.Muted;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  ");
            _log.SelectionColor = color;
            _log.AppendText(msg + Environment.NewLine);
            _log.SelectionStart = _log.TextLength;
            _log.ScrollToCaret();
        }

        internal static void OpenPath(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Pocket Dispatch"); }
        }
    }
}
