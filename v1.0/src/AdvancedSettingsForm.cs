using System.Drawing;
using System.Windows.Forms;

internal sealed class AdvancedSettingsForm : Form
{
    private readonly NumericUpDown count = new NumericUpDown();
    private readonly NumericUpDown interval = new NumericUpDown();

    internal int Concurrency { get { return (int)count.Value; } }
    internal int IntervalMs { get { return (int)interval.Value; } }

    internal AdvancedSettingsForm(int concurrency, int intervalMs, AppTheme theme)
    {
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "高级设置";
        Font = new Font("Microsoft YaHei UI", 10f);
        ClientSize = new Size(585, 233);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = theme.Background;
        ForeColor = theme.Text;
        HandleCreated += (sender, e) => theme.ApplyTitleBar(this);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20),
            ColumnCount = 2, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var warning = new Label
        {
            Text = "以下的设置选项仅供测试使用，随意调整可能导致程序卡顿或美团风控检测，请谨慎修改",
            ForeColor = theme.Failed, Font = new Font(Font, FontStyle.Bold),
            Dock = DockStyle.Fill
        };
        layout.Controls.Add(warning, 0, 0);
        layout.SetColumnSpan(warning, 2);

        count.Minimum = 1;
        count.Maximum = 20;
        count.Value = concurrency;
        count.AccessibleName = "同时并发查询进程数量";
        count.BackColor = theme.Surface;
        count.ForeColor = theme.Text;
        interval.Minimum = 0;
        interval.Maximum = 10000;
        interval.Value = intervalMs;
        interval.AccessibleName = "并发进程间隔（ms）";
        interval.BackColor = theme.Surface;
        interval.ForeColor = theme.Text;
        layout.Controls.Add(FieldLabel("同时并发查询进程数量（1–20）"), 0, 1);
        layout.Controls.Add(count, 1, 1);
        layout.Controls.Add(FieldLabel("并发进程间隔（ms，0–10000）"), 0, 2);
        layout.Controls.Add(interval, 1, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "应用", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, AutoSize = true };
        theme.StyleButton(save, true);
        theme.StyleButton(cancel, false);
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 3);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = save;
        CancelButton = cancel;
        ResumeLayout(true);
    }

    private static Label FieldLabel(string text)
    {
        return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft };
    }
}
