using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class AppTheme
{
    internal static readonly AppTheme Light = new AppTheme(false);
    internal static readonly AppTheme Dark = new AppTheme(true);
    internal readonly bool IsDark;
    internal readonly Color Background, Surface, Stripe, Text, Muted, Accent, Failed, Link, Border, Hover, Icon;

    private AppTheme(bool dark)
    {
        IsDark = dark;
        Background = dark ? Color.FromArgb(21, 25, 33) : Color.FromArgb(242, 246, 250);
        Surface = dark ? Color.FromArgb(30, 37, 48) : Color.White;
        Stripe = dark ? Color.FromArgb(37, 45, 58) : Color.FromArgb(245, 248, 251);
        Text = dark ? Color.FromArgb(239, 243, 249) : Color.FromArgb(20, 34, 52);
        Muted = dark ? Color.FromArgb(184, 198, 216) : Color.FromArgb(84, 101, 123);
        Accent = dark ? Color.FromArgb(112, 225, 184) : Color.FromArgb(0, 107, 95);
        Failed = dark ? Color.FromArgb(255, 155, 163) : Color.FromArgb(170, 32, 32);
        Link = dark ? Color.FromArgb(147, 198, 255) : Color.FromArgb(0, 91, 157);
        Border = dark ? Color.FromArgb(119, 137, 160) : Color.FromArgb(157, 173, 192);
        Hover = dark ? Color.FromArgb(50, 64, 83) : Color.FromArgb(227, 237, 247);
        Icon = dark ? Color.FromArgb(255, 215, 115) : Color.FromArgb(43, 75, 125);
    }

    internal void StyleButton(Button button, bool primary)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = primary ? 0 : 1;
        button.FlatAppearance.BorderColor = Border;
        button.BackColor = primary ? Accent : Surface;
        button.ForeColor = primary ? (IsDark ? Background : Color.White) : Text;
        button.FlatAppearance.MouseOverBackColor = primary
            ? (IsDark ? Color.FromArgb(142, 239, 204) : Color.FromArgb(0, 89, 80)) : Hover;
        button.FlatAppearance.MouseDownBackColor = button.FlatAppearance.MouseOverBackColor;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    internal void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        const int ImmersiveDarkMode = 20;
        int enabled = IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, ImmersiveDarkMode, ref enabled, sizeof(int));
    }
}

internal sealed class ThemeButton : Button
{
    private AppTheme theme = AppTheme.Light;

    internal void ApplyTheme(AppTheme value)
    {
        theme = value;
        theme.StyleButton(this, false);
        AccessibleName = theme.IsDark ? "切换至浅色模式" : "切换至暗黑模式";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float scale = Math.Min(ClientSize.Width, ClientSize.Height) / 38f;
        float x = ClientSize.Width / 2f;
        float y = ClientSize.Height / 2f;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (theme.IsDark)
        {
            using (var pen = new Pen(theme.Icon, 1.8f * scale))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                float radius = 5.3f * scale;
                e.Graphics.DrawEllipse(pen, x - radius, y - radius, radius * 2, radius * 2);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4;
                    float dx = (float)Math.Cos(angle) * scale;
                    float dy = (float)Math.Sin(angle) * scale;
                    e.Graphics.DrawLine(pen, x + dx * 8, y + dy * 8, x + dx * 11, y + dy * 11);
                }
            }
        }
        else
        {
            float radius = 9.5f * scale;
            using (var outer = new GraphicsPath())
            using (var cutout = new GraphicsPath())
            using (var brush = new SolidBrush(theme.Icon))
            {
                outer.AddEllipse(x - radius, y - radius, radius * 2, radius * 2);
                cutout.AddEllipse(x - radius + 6.5f * scale, y - radius - 4.2f * scale, radius * 2, radius * 2);
                using (var crescent = new Region(outer))
                {
                    crescent.Exclude(cutout);
                    e.Graphics.FillRegion(brush, crescent);
                }
            }
        }
    }
}
