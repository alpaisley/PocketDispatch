using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PocketDispatch.Core;

namespace PocketDispatch.App
{
    /// <summary>Dark settings dialog.</summary>
    internal sealed class SettingsForm : Form
    {
        readonly AppSettings _s;
        readonly TextBox _pilotId = new TextBox();
        readonly TextBox _username = new TextBox();
        readonly TextBox _xplane = new TextBox();
        readonly TextBox _avitab = new TextBox();
        readonly TextBox _fms = new TextBox();
        readonly ComboBox _aircraft = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox _a5 = new CheckBox { Text = "A5 pages - bigger text on the AviTab screen", AutoSize = true };
        readonly CheckBox _keepPdf = new CheckBox { Text = "Also save SimBrief's full OFP PDF", AutoSize = true };
        readonly CheckBox _startMin = new CheckBox { Text = "Start minimised to the tray", AutoSize = true };

        public SettingsForm(AppSettings s)
        {
            _s = s;
            Text = "Pocket Dispatch - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 500);
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.UI(9.5f);
            ShowInTaskbar = false;

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(20, 16, 20, 4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));

            Action<string> section = title =>
            {
                var l = new Label { Text = title, AutoSize = true, ForeColor = Theme.Amber, Font = Theme.Din(10.5f, FontStyle.Bold), Margin = new Padding(0, 10, 0, 4) };
                grid.Controls.Add(l);
                grid.SetColumnSpan(l, 3);
            };
            Action<string, Control, Control> row = (label, c, extra) =>
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, ForeColor = Theme.Muted, Anchor = AnchorStyles.Left, Margin = new Padding(0, 7, 0, 0) });
                c.Dock = DockStyle.Fill;
                Theme.StyleInput(c);
                grid.Controls.Add(c);
                grid.Controls.Add(extra ?? new Label());
            };
            Func<TextBox, Button> browse = target =>
            {
                var b = Theme.Button("Browse", false);
                b.AutoSize = false;
                b.Dock = DockStyle.Fill;
                b.Margin = new Padding(6, 2, 0, 2);
                b.Padding = new Padding(0);
                b.Click += (s2, e2) =>
                {
                    using (var d = new FolderBrowserDialog { SelectedPath = target.Text })
                        if (d.ShowDialog(this) == DialogResult.OK) target.Text = d.SelectedPath;
                };
                return b;
            };

            section("SIMBRIEF");
            row("Pilot ID", _pilotId, null);
            row("or username", _username, null);
            section("FOLDERS");
            row("X-Plane", _xplane, browse(_xplane));
            row("AviTab charts", _avitab, browse(_avitab));
            row("FMS plans", _fms, browse(_fms));
            section("BRIEF");
            _aircraft.Items.AddRange(new object[] { "Auto-detect", "erj", "toliss", "q400", "generic" });
            row("Aircraft layout", _aircraft, null);
            var checks = new FlowLayoutPanel { AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0, 4, 0, 0) };
            foreach (var cb in new[] { _a5, _keepPdf, _startMin }) { cb.ForeColor = Theme.Text; checks.Controls.Add(cb); }
            grid.Controls.Add(new Label());
            grid.Controls.Add(checks);
            grid.Controls.Add(new Label());

            var hint = new Label
            {
                Text = "Leave AviTab / FMS blank for the standard X-Plane folders.\nYour Pilot ID is on simbrief.com > Account Settings.",
                AutoSize = false, Dock = DockStyle.Bottom, Height = 40, ForeColor = Theme.Muted, Padding = new Padding(20, 0, 20, 0), Font = Theme.UI(8.5f),
            };

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 56, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 8, 16, 8) };
            var save = Theme.Button("Save", true);
            var cancel = Theme.Button("Cancel", false);
            var layouts = Theme.Button("Edit aircraft layouts...", false);
            buttons.Controls.Add(save);
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(layouts);

            Controls.Add(grid);
            Controls.Add(hint);
            Controls.Add(buttons);

            _pilotId.Text = s.PilotId;
            _username.Text = s.Username;
            _xplane.Text = s.XPlaneFolder;
            _avitab.Text = s.AviTabFolder;
            _fms.Text = s.FmsFolder;
            _aircraft.SelectedItem = string.IsNullOrEmpty(s.Aircraft) ? "Auto-detect" : s.Aircraft;
            if (_aircraft.SelectedIndex < 0) _aircraft.SelectedIndex = 0;
            _a5.Checked = s.A5;
            _keepPdf.Checked = s.KeepOriginalPdf;
            _startMin.Checked = s.StartMinimised;

            AcceptButton = save;
            CancelButton = cancel;
            cancel.DialogResult = DialogResult.Cancel;
            save.Click += (s2, e2) =>
            {
                if (!Directory.Exists(Path.Combine(_xplane.Text.Trim(), "Resources")))
                {
                    MessageBox.Show(this, "That X-Plane folder doesn't contain a Resources folder.", "Pocket Dispatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                _s.PilotId = _pilotId.Text.Trim();
                _s.Username = _username.Text.Trim();
                _s.XPlaneFolder = _xplane.Text.Trim();
                _s.AviTabFolder = _avitab.Text.Trim();
                _s.FmsFolder = _fms.Text.Trim();
                _s.Aircraft = _aircraft.SelectedIndex <= 0 ? "" : (string)_aircraft.SelectedItem;
                _s.A5 = _a5.Checked;
                _s.KeepOriginalPdf = _keepPdf.Checked;
                _s.StartMinimised = _startMin.Checked;
                _s.Save();
                DialogResult = DialogResult.OK;
                Close();
            };
            layouts.Click += (s2, e2) =>
            {
                Profiles.Load(AppSettings.Folder);   // writes the defaults if missing
                MainForm.OpenPath(Path.Combine(AppSettings.Folder, "profiles.json"));
            };
            HandleCreated += (s2, e2) => Theme.DarkTitleBar(this);
        }
    }
}
