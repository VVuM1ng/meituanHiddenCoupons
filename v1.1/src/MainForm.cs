using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

[assembly: AssemblyVersion("1.1.0.0")]

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string MutedLabel = "muted";
    private const string Instructions = "请输入美团外卖商家的分享链接，若要批量查询，请每行输入一个分享链接。\r\n"
        + "分享链接格式：分享给你美团外卖的「商家」，点击链接可进店查看详情  http://dpurl.cn/********";
    private const string ClaimHint = "左键打开，右键复制链接";
    private const string GitHubProfile = "https://github.com/VVuM1ng";
    private static readonly Size LogicalMinimumSize = new Size(790, 635);
    private readonly QueryTextBox input = new QueryTextBox();
    private readonly TextBox search = new TextBox();
    private readonly ComboBox sorting = new ComboBox();
    private readonly CheckBox[] filters = new CheckBox[7];
    private readonly Button start = new Button();
    private readonly Button reuseQuery = new Button();
    private readonly Button github = new Button();
    private readonly ThemeButton themeToggle = new ThemeButton();
    private readonly TableLayoutPanel layout;
    private readonly Label status = new Label();
    private readonly Label visibleCount = new Label();
    private readonly Panel output = new Panel();
    private readonly ToolTip explanations = new ToolTip();
    private readonly ToolTip claimTips = new ToolTip { OwnerDraw = true, BackColor = Color.White, ForeColor = Color.Black };
    private readonly List<WorkItem> results = new List<WorkItem>();
    private readonly UserSettings settings = UserSettings.Load();
    private readonly Icon appIcon;
    private AppTheme theme;
    private CancellationTokenSource active;
    private bool closing, resizeQueued;
    private int displayDpi = 96;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private sealed class WorkItem
    {
        internal int Index;
        internal Store Store;
        internal LookupResult Result;
        internal string Input, Name, Initials, HanCharacters;
    }

    internal MainForm()
    {
        theme = settings.DarkMode ? AppTheme.Dark : AppTheme.Light;
        settings.Concurrency = Math.Max(1, Math.Min(20, settings.Concurrency));
        settings.IntervalMs = Math.Max(0, Math.Min(10000, settings.IntervalMs));
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "外卖商家券";
        appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (appIcon != null) Icon = appIcon;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1000, 755);
        MinimumSize = LogicalMinimumSize;
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = theme.Background;
        ForeColor = theme.Text;

        layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(28, 20, 28, 16),
            ColumnCount = 1, RowCount = 12
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 43, 29, 32 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        foreach (int height in new[] { 54, 50, 36, 30, 40, 34 })
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        Controls.Add(layout);
        layout.Controls.Add(CreateTitle(), 0, 0);
        var subtitle = new LinkLabel
        {
            Text = "批量查询美团外卖门店的专享商家券 / from Github @VVuM1ng",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
            Tag = MutedLabel, LinkBehavior = LinkBehavior.AlwaysUnderline
        };
        subtitle.LinkArea = new LinkArea(subtitle.Text.IndexOf('@'), "@VVuM1ng".Length);
        subtitle.LinkClicked += (sender, e) => OpenBrowser(GitHubProfile);
        layout.Controls.Add(subtitle, 0, 1);
        layout.Controls.Add(Heading("门店链接", 12f), 0, 2);

        input.Multiline = true;
        input.WordWrap = false;
        input.ScrollBars = ScrollBars.Both;
        input.AcceptsReturn = true;
        input.Dock = DockStyle.Fill;
        input.BackColor = theme.Surface;
        input.BorderStyle = BorderStyle.FixedSingle;
        input.Margin = new Padding(0);
        input.AccessibleName = "门店链接输入框";
        layout.Controls.Add(input, 0, 3);
        layout.Controls.Add(Caption(Instructions), 0, 4);
        layout.Controls.Add(CreateActions(), 0, 5);

        var resultHeading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Margin = new Padding(0), ColumnCount = 2, RowCount = 1
        };
        resultHeading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        resultHeading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resultHeading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultHeading.Controls.Add(Heading("查询结果", 12f), 0, 0);
        visibleCount.AutoSize = true;
        visibleCount.Anchor = AnchorStyles.Right;
        visibleCount.Tag = MutedLabel;
        resultHeading.Controls.Add(visibleCount, 1, 0);
        layout.Controls.Add(resultHeading, 0, 6);
        var resultHint = Caption("有时提示“无商家券”可能是因为请求失败，可以点击“重新请求”或点击“领取商家券”自行查看是否有券。");
        resultHint.Font = new Font(Font.FontFamily, 9f);
        layout.Controls.Add(resultHint, 0, 7);
        layout.Controls.Add(CreateSearchAndSort(), 0, 8);
        layout.Controls.Add(CreateFilters(), 0, 9);

        output.Dock = DockStyle.Fill;
        output.AutoScroll = true;
        output.BackColor = theme.Surface;
        output.BorderStyle = BorderStyle.FixedSingle;
        output.Margin = new Padding(0);
        output.Padding = new Padding(9, 4, 9, 4);
        output.AccessibleName = "商家券查询结果";
        output.HandleCreated += (sender, e) => theme.StyleNativeControl(output);
        output.SizeChanged += (sender, e) => QueueResultResize();
        layout.Controls.Add(output, 0, 10);
        status.Dock = DockStyle.Fill;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Tag = MutedLabel;
        status.Text = "输入链接后点击「开始查询」。";
        layout.Controls.Add(status, 0, 11);

        claimTips.Draw += (sender, e) =>
        {
            e.DrawBackground();
            e.DrawBorder();
            e.DrawText();
        };
        search.TextChanged += (sender, e) => RefreshResults();
        sorting.SelectedIndexChanged += (sender, e) => RefreshResults();
        FormClosing += (sender, e) =>
        {
            closing = true;
            if (active != null) active.Cancel();
            SaveSettings();
        };
        FormClosed += (sender, e) =>
        {
            explanations.Dispose();
            claimTips.Dispose();
            if (github.Image != null) github.Image.Dispose();
            if (appIcon != null) appIcon.Dispose();
        };
        HandleCreated += (sender, e) => theme.ApplyTitleBar(this);
        Load += (sender, e) => UpdateWindowForDisplay();
        ApplyTheme();
        ResumeLayout(true);
    }

    private Control CreateTitle()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(Heading("外卖商家券", 23f), 0, 0);
        github.Text = "在 Github 上查看项目";
        github.Font = new Font(Font, FontStyle.Bold);
        github.AutoSize = true;
        github.MinimumSize = new Size(185, 36);
        github.Anchor = AnchorStyles.Right;
        github.Margin = new Padding(0, 0, 4, 0);
        github.Padding = new Padding(8, 0, 8, 0);
        github.TextImageRelation = TextImageRelation.ImageBeforeText;
        github.ImageAlign = ContentAlignment.MiddleRight;
        github.TextAlign = ContentAlignment.MiddleLeft;
        using (var resource = typeof(MainForm).Assembly.GetManifestResourceStream("mark-github.png"))
        using (var image = Image.FromStream(resource))
            github.Image = new Bitmap(image);
        github.Click += (sender, e) => OpenBrowser(GitHubProfile + "/meituanHiddenCoupons");
        header.Controls.Add(github, 1, 0);
        themeToggle.Size = new Size(42, 36);
        themeToggle.Anchor = AnchorStyles.Right;
        themeToggle.Margin = new Padding(0);
        themeToggle.Click += (sender, e) =>
        {
            theme = theme.IsDark ? AppTheme.Light : AppTheme.Dark;
            ApplyTheme();
        };
        header.Controls.Add(themeToggle, 2, 0);
        return header;
    }

    private void ApplyTheme()
    {
        SuspendLayout();
        try
        {
            BackColor = theme.Background;
            ForeColor = theme.Text;
            ApplyThemeToControls(layout);
            input.ApplyTheme(theme);
            sorting.FlatStyle = theme.IsDark ? FlatStyle.Standard : FlatStyle.Flat;
            theme.StyleNativeControl(sorting);
            theme.StyleNativeControl(output);
            AppTheme.Light.StyleButton(github, false);
            github.ForeColor = Color.Black;
            themeToggle.ApplyTheme(theme);
            explanations.SetToolTip(themeToggle, themeToggle.AccessibleName);
            theme.ApplyTitleBar(this);
            int scrollY = -output.AutoScrollPosition.Y;
            RefreshResults();
            output.AutoScrollPosition = new Point(0, scrollY);
            sorting.Invalidate();
        }
        finally { ResumeLayout(true); }
    }

    private void ApplyThemeToControls(Control control)
    {
        if (control == output)
        {
            output.BackColor = theme.Surface;
            return;
        }
        var button = control as Button;
        if (button != null)
        {
            if (button != themeToggle && button != github) theme.StyleButton(button, button == start);
            return;
        }
        control.ForeColor = Equals(control.Tag, MutedLabel) ? theme.Muted : theme.Text;
        if (control is TextBox || control is ComboBox) control.BackColor = theme.Surface;
        var link = control as LinkLabel;
        if (link != null)
        {
            link.LinkColor = theme.Link;
            link.ActiveLinkColor = theme.Accent;
        }
        var filter = control as CheckBox;
        if (filter != null)
        {
            filter.FlatStyle = theme.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
            filter.FlatAppearance.BorderColor = theme.Border;
            filter.FlatAppearance.CheckedBackColor = theme.Hover;
        }
        foreach (Control child in control.Controls) ApplyThemeToControls(child);
    }

    private Control CreateActions()
    {
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        start.Text = "开始查询";
        start.AutoSize = true;
        start.MinimumSize = new Size(110, 36);
        start.FlatStyle = FlatStyle.Flat;
        start.FlatAppearance.BorderSize = 0;
        start.Margin = new Padding(0, 4, 10, 0);
        start.Click += async (sender, e) => await RunBatch();
        actions.Controls.Add(start);

        reuseQuery.Text = "一键添加上次查询的店铺";
        reuseQuery.AutoSize = true;
        reuseQuery.MinimumSize = new Size(0, 36);
        reuseQuery.Margin = new Padding(0, 4, 10, 0);
        reuseQuery.Click += (sender, e) =>
        {
            string text = UserSettings.ReadLastQuery();
            if (string.IsNullOrEmpty(text))
            {
                status.Text = "暂无可添加的上次查询记录。";
                return;
            }
            input.Text = text;
            input.SelectionStart = input.TextLength;
            input.Focus();
        };
        actions.Controls.Add(reuseQuery);

        var advanced = new Button
        {
            Text = "高级设置", AutoSize = true, MinimumSize = new Size(100, 36),
            Margin = new Padding(0, 4, 0, 0)
        };
        advanced.Click += (sender, e) =>
        {
            using (var dialog = new AdvancedSettingsForm(settings.Concurrency, settings.IntervalMs, theme))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                settings.Concurrency = dialog.Concurrency;
                settings.IntervalMs = dialog.IntervalMs;
            }
        };
        actions.Controls.Add(advanced);
        return actions;
    }

    private Control CreateSearchAndSort()
    {
        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), ColumnCount = 4 };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        bar.Controls.Add(BarLabel("搜索店名"), 0, 0);
        search.Dock = DockStyle.Fill;
        search.BorderStyle = BorderStyle.FixedSingle;
        search.AccessibleName = "搜索店名";
        search.Margin = new Padding(0, 5, 16, 0);
        bar.Controls.Add(search, 1, 0);
        bar.Controls.Add(BarLabel("排序"), 2, 0);
        sorting.DropDownStyle = ComboBoxStyle.DropDownList;
        sorting.DrawMode = DrawMode.OwnerDrawFixed;
        sorting.DrawItem += DrawSortingItem;
        sorting.HandleCreated += (sender, e) => theme.StyleNativeControl(sorting);
        sorting.FlatStyle = FlatStyle.Flat;
        sorting.Dock = DockStyle.Fill;
        sorting.AccessibleName = "查询结果排序";
        sorting.Margin = new Padding(0, 5, 0, 0);
        sorting.Items.AddRange(new object[] { "默认顺序", "店名拼音首字母 A–Z", "券额从大到小", "券额从小到大" });
        sorting.SelectedIndex = Math.Max(1, Math.Min(sorting.Items.Count, settings.SortIndex)) - 1;
        bar.Controls.Add(sorting, 3, 0);
        return bar;
    }

    private void DrawSortingItem(object sender, DrawItemEventArgs e)
    {
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var fill = new SolidBrush(selected ? theme.Hover : theme.Surface))
            e.Graphics.FillRectangle(fill, e.Bounds);
        if (e.Index >= 0)
        {
            var bounds = e.Bounds;
            bounds.Inflate(-ScalePixels(4), 0);
            TextRenderer.DrawText(e.Graphics, sorting.Items[e.Index].ToString(), e.Font, bounds, theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
        e.DrawFocusRectangle();
    }

    private Control CreateFilters()
    {
        var bar = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), WrapContents = false };
        bar.Controls.Add(new Label { Text = "筛选", AutoSize = true, Tag = MutedLabel, Margin = new Padding(0, 6, 11, 0) });
        for (int i = 0; i < filters.Length; i++)
        {
            var filter = new CheckBox
            {
                Text = i < 5 ? (5 - i) + "元商家券" : i == 5 ? "无商家券" : "请求失败",
                Checked = (settings.FilterMask & (1 << i)) != 0, AutoSize = true, Margin = new Padding(0, 4, 11, 0)
            };
            filters[i] = filter;
            filter.CheckedChanged += (sender, e) => RefreshResults();
            bar.Controls.Add(filter);
        }
        return bar;
    }

    private void SaveSettings()
    {
        settings.DarkMode = theme.IsDark;
        settings.SortIndex = sorting.SelectedIndex + 1;
        settings.FilterMask = 0;
        for (int i = 0; i < filters.Length; i++)
            if (filters[i].Checked) settings.FilterMask |= 1 << i;
        settings.Save();
    }

    private async Task RunBatch()
    {
        if (active != null || closing) return;
        string submitted = input.Text;
        bool cacheSaved = UserSettings.SaveLastQuery(submitted);
        var lines = input.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
        if (lines.Length == 0)
        {
            status.Text = "请先输入至少一条门店分享链接。" + (cacheSaved ? "" : "本次输入未能缓存。");
            input.Focus();
            return;
        }

        int batchSize = settings.Concurrency;
        int gap = batchSize > 1 ? settings.IntervalMs : 0;
        using (var batch = new CancellationTokenSource())
        {
            active = batch;
            start.Enabled = false;
            results.Clear();
            RefreshResults();
            try
            {
                for (int startIndex = 0; startIndex < lines.Length; startIndex += batchSize)
                {
                    int size = Math.Min(batchSize, lines.Length - startIndex);
                    status.Text = string.Format("正在后台查询第 {0}–{1} 家，共 {2} 家…", startIndex + 1, startIndex + size, lines.Length);
                    var work = await Task.WhenAll(Enumerable.Range(startIndex, size)
                        .Select(index => ResolveStore(lines[index], index, batch.Token)));
                    await RunWave(work, gap, batch.Token);
                    batch.Token.ThrowIfCancellationRequested();
                    results.AddRange(work);
                    RefreshResults();
                }
                ShowCompletedStatus();
                if (!cacheSaved) status.Text += "  ·  本次输入未能缓存";
            }
            catch (OperationCanceledException)
            {
                if (!batch.IsCancellationRequested) throw;
            }
            finally
            {
                active = null;
                if (!closing)
                {
                    start.Enabled = true;
                    RefreshResults();
                }
            }
        }
    }

    private static async Task<WorkItem> ResolveStore(string line, int index, CancellationToken token)
    {
        var work = new WorkItem { Index = index, Input = line };
        try { work.Store = await StoreLookup.ResolveAsync(line, token); }
        catch (Exception error)
        {
            token.ThrowIfCancellationRequested();
            SetResult(work, Failure(null, StoreLookup.ReadName(line), error));
        }
        return work;
    }

    private async Task RetryStore(WorkItem item)
    {
        if (active != null || closing || !results.Contains(item) || item.Result.Amount.HasValue) return;
        using (var retry = new CancellationTokenSource())
        {
            active = retry;
            start.Enabled = false;
            status.Text = "正在重新请求「" + item.Name + "」…";
            RefreshResults();
            try
            {
                var updated = await ResolveStore(item.Input, item.Index, retry.Token);
                if (updated.Store != null) await QueryStore(updated, retry.Token);
                retry.Token.ThrowIfCancellationRequested();
                results[results.IndexOf(item)] = updated;
                ShowCompletedStatus();
            }
            catch (OperationCanceledException)
            {
                if (!retry.IsCancellationRequested) throw;
            }
            finally
            {
                active = null;
                if (!closing)
                {
                    start.Enabled = true;
                    RefreshResults();
                }
            }
        }
    }

    private void ShowCompletedStatus()
    {
        int found = results.Count(item => item.Result.Amount.HasValue);
        int empty = results.Count(item => item.Result.NoCoupon);
        status.Text = string.Format("查询完成  ·  有券 {0}  ·  无券 {1}  ·  请求失败 {2}",
            found, empty, results.Count - found - empty);
    }

    private async Task RunWave(WorkItem[] work, int gap, CancellationToken token)
    {
        var running = new List<Task>();
        foreach (var item in work)
        {
            if (item.Store == null) continue;
            if (running.Count > 0 && gap > 0)
            {
                try { await Task.Delay(gap, token); }
                catch (OperationCanceledException) { break; }
            }
            if (token.IsCancellationRequested) break;
            running.Add(QueryStore(item, token));
        }
        await Task.WhenAll(running);
        token.ThrowIfCancellationRequested();
    }

    private async Task QueryStore(WorkItem item, CancellationToken token)
    {
        // Each concurrent query has its own WebView. Its viewport is clipped outside the form.
        try
        {
            using (var browser = new WebView2 { Bounds = new Rectangle(-840, 0, 800, 800), TabStop = false })
            {
                Controls.Add(browser);
                SetResult(item, await new StoreLookup(browser).QueryAsync(item.Store, token));
            }
        }
        catch (Exception error)
        {
            token.ThrowIfCancellationRequested();
            SetResult(item, Failure(item.Store, item.Store.Name, error));
        }
    }

    private static LookupResult Failure(Store store, string name, Exception error)
    {
        return new LookupResult
        {
            Name = name,
            CouponLink = store == null ? null : store.CouponLink,
            SecondLink = store == null ? null : store.SecondLink,
            Detail = error is FormatException ? error.Message
                : error is OperationCanceledException ? "查询超时。" : "无法读取美团页面，请稍后重试。"
        };
    }

    private static void SetResult(WorkItem work, LookupResult result)
    {
        work.Result = result;
        work.Name = string.IsNullOrWhiteSpace(result.Name) ? "第 " + (work.Index + 1) + " 条门店" : result.Name;
        work.Initials = StoreNameOrder.Initials(work.Name, out work.HanCharacters);
    }

    private void RefreshResults()
    {
        string text = search.Text.Trim().Normalize(NormalizationForm.FormKC);
        bool allFilters = filters.All(filter => filter.Checked);
        var filtered = results.Where(item =>
            (allFilters || IsSelected(item.Result))
            && (text.Length == 0 || item.Name.Normalize(NormalizationForm.FormKC).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0));
        var sorted = SortResults(filtered, sorting.SelectedIndex).ToArray();
        visibleCount.Text = string.Format("显示 {0} / {1}", sorted.Length, results.Count);
        output.SuspendLayout();
        try
        {
            output.AutoScrollPosition = Point.Empty;
            claimTips.RemoveAll();
            while (output.Controls.Count > 0) output.Controls[0].Dispose();
            output.AutoScrollMinSize = Size.Empty;
            if (sorted.Length == 0)
            {
                var placeholder = Caption(results.Count == 0 ? "查询结果会显示在这里。" : "没有符合当前筛选条件的门店。");
                placeholder.Dock = DockStyle.None;
                placeholder.AutoSize = true;
                placeholder.Location = new Point(ScalePixels(18), ScalePixels(12));
                output.Controls.Add(placeholder);
                return;
            }
            output.AutoScrollMinSize = new Size(0,
                output.Padding.Vertical + sorted.Length * ScalePixels(43));
            for (int i = 0; i < sorted.Length; i++) AddResult(sorted[i], i);
        }
        finally { output.ResumeLayout(true); }
    }

    private bool IsSelected(LookupResult result)
    {
        if (result.Amount.HasValue)
        {
            decimal amount = result.Amount.Value;
            return amount >= 1 && amount <= 5 && decimal.Truncate(amount) == amount && filters[5 - (int)amount].Checked;
        }
        return filters[result.NoCoupon ? 5 : 6].Checked;
    }

    private static IEnumerable<WorkItem> SortResults(IEnumerable<WorkItem> items, int order)
    {
        switch (order)
        {
            case 1:
                return items.OrderBy(item => item.Initials, StringComparer.Ordinal)
                    .ThenBy(item => item.HanCharacters, StringComparer.Ordinal).ThenBy(item => item.Index);
            case 2:
                return items.OrderBy(item => !item.Result.Amount.HasValue)
                    .ThenByDescending(item => item.Result.Amount).ThenBy(item => item.Index);
            case 3:
                return items.OrderBy(item => !item.Result.Amount.HasValue)
                    .ThenBy(item => item.Result.Amount).ThenBy(item => item.Index);
            default:
                return items.OrderBy(item => item.Index);
        }
    }

    private void AddResult(WorkItem item, int position)
    {
        var row = new TableLayoutPanel
        {
            ColumnCount = 3, RowCount = 1,
            Padding = new Padding(ScalePixels(10), 0, ScalePixels(8), 0),
            Margin = new Padding(0),
            Location = new Point(output.Padding.Left, output.Padding.Top + position * ScalePixels(43)),
            Height = ScalePixels(39),
            BackColor = position % 2 == 0 ? theme.Stripe : theme.Surface
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, ScalePixels(86)));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var result = item.Result;
        string state = result.Amount.HasValue
            ? result.Amount.Value.ToString("0.##", CultureInfo.InvariantCulture) + "元商家券"
            : result.NoCoupon ? "无商家券" : "请求失败";
        var summary = new Label
        {
            Text = item.Name + "：" + state, AutoEllipsis = true, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0),
            ForeColor = result.Amount.HasValue ? theme.Accent
                : result.NoCoupon ? (theme.IsDark ? theme.Text : Color.Black) : theme.Failed
        };
        if (result.Detail != null)
        {
            summary.AccessibleDescription = result.Detail;
            explanations.SetToolTip(summary, result.Detail);
        }
        row.Controls.Add(summary, 0, 0);
        if (result.CouponLink != null && result.SecondLink != null)
        {
            var links = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Anchor = AnchorStyles.Right, Margin = new Padding(ScalePixels(12), 0, 0, 0)
            };
            links.Controls.Add(ClaimLink("领取商家券", result.CouponLink));
            links.Controls.Add(new Label { Text = "|", ForeColor = theme.Muted, AutoSize = true, Margin = new Padding(3, 0, 3, 0) });
            links.Controls.Add(ClaimLink("二次领取", result.SecondLink));
            row.Controls.Add(links, 1, 0);
        }
        if (!result.Amount.HasValue)
        {
            var retryLink = ActionLink("重新请求");
            retryLink.Anchor = AnchorStyles.Right;
            retryLink.Enabled = active == null;
            retryLink.LinkClicked += async (sender, e) => await RetryStore(item);
            row.Controls.Add(retryLink, 2, 0);
        }
        output.Controls.Add(row);
        SizeRow(row);
    }

    private LinkLabel ActionLink(string title)
    {
        return new LinkLabel
        {
            Text = title, AutoSize = true, LinkColor = theme.Link, ActiveLinkColor = theme.Accent,
            DisabledLinkColor = theme.Muted, VisitedLinkColor = theme.Link,
            LinkBehavior = LinkBehavior.AlwaysUnderline, Margin = new Padding(3, 0, 3, 0)
        };
    }

    private LinkLabel ClaimLink(string title, string url)
    {
        var link = ActionLink(title);
        link.AccessibleDescription = ClaimHint;
        claimTips.SetToolTip(link, ClaimHint);
        link.LinkClicked += (sender, e) =>
        {
            if (e.Button != MouseButtons.Right) OpenBrowser(url);
        };
        link.MouseUp += (sender, e) =>
        {
            if (e.Button != MouseButtons.Right || !link.ClientRectangle.Contains(e.Location)) return;
            try
            {
                Clipboard.SetText(url);
                status.Text = "已复制「" + title + "」链接。";
            }
            catch (ExternalException) { status.Text = "无法访问剪贴板，请重试。"; }
        };
        return link;
    }

    private void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { status.Text = "无法打开默认浏览器。"; }
        catch (InvalidOperationException) { status.Text = "无法打开默认浏览器。"; }
    }

    private void QueueResultResize()
    {
        if (resizeQueued || !output.IsHandleCreated || closing) return;
        resizeQueued = true;
        output.BeginInvoke(new Action(() =>
        {
            resizeQueued = false;
            if (closing || output.IsDisposed) return;
            int scrollY = -output.AutoScrollPosition.Y;
            output.SuspendLayout();
            output.AutoScroll = false;
            foreach (var row in output.Controls.OfType<TableLayoutPanel>()) SizeRow(row);
            output.AutoScroll = true;
            output.ResumeLayout(true);
            output.AutoScrollPosition = new Point(0, scrollY);
        }));
    }

    private void SizeRow(TableLayoutPanel row)
    {
        row.Width = Math.Max(ScalePixels(150), output.ClientSize.Width - output.Padding.Horizontal
            - SystemInformation.VerticalScrollBarWidth);
    }

    private int ScalePixels(int logical)
    {
        return (int)Math.Round(logical * displayDpi / 96.0);
    }

    private void UpdateWindowForDisplay()
    {
        UpdateDisplayDpi();
        if (closing || WindowState != FormWindowState.Normal) return;

        Rectangle workArea = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(ScalePixels(LogicalMinimumSize.Width), workArea.Width),
            Math.Min(ScalePixels(LogicalMinimumSize.Height), workArea.Height));
        int width = Math.Min(Width, workArea.Width);
        int height = Math.Min(Height, workArea.Height);
        if (width == Width && height == Height) return;

        int left = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - width));
        int top = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - height));
        SetBounds(left, top, width, height);
    }

    private void UpdateDisplayDpi()
    {
        if (closing || !IsHandleCreated) return;
        int dpi = DeviceDpi;
        try
        {
            uint measured = GetDpiForWindow(Handle);
            if (measured > 0) dpi = (int)measured;
        }
        catch (EntryPointNotFoundException) { }
        if (dpi == displayDpi) return;
        displayDpi = dpi;
        RefreshResults();
    }

    protected override void WndProc(ref Message message)
    {
        const int DpiChanged = 0x02e0;
        base.WndProc(ref message);
        if (message.Msg == DpiChanged && IsHandleCreated && !closing)
            BeginInvoke(new Action(UpdateWindowForDisplay));
    }

    private Label Heading(string text, float size)
    {
        return new Label
        {
            Text = text, Font = new Font(Font.FontFamily, size, FontStyle.Bold),
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0)
        };
    }

    private Label Caption(string text)
    {
        return new Label
        {
            Text = text, Dock = DockStyle.Fill, ForeColor = theme.Muted, Tag = MutedLabel,
            TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0)
        };
    }

    private static Label BarLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
            Tag = MutedLabel, Margin = new Padding(0, 0, 10, 0) };
    }
}
