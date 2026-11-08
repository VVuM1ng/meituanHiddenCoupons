using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class Store
{
    internal string Id, Name, CouponLink, SecondLink;
}

internal sealed class LookupResult
{
    internal string Name, Detail, CouponLink, SecondLink;
    internal decimal? Amount;
    internal bool NoCoupon;
}

internal sealed class StoreLookup
{
    private const string ActivityHost = "offsiteact.meituan.com";
    private const string ActivityPath = "/web/hoae/collection_waimai_v8/index.html";
    private const string DataPath = "/act/ge/queryPoiByRecallBiz";
    private const string CouponTemplate = "https://offsiteact.meituan.com/web/hoae/collection_waimai_v8/index.html?recallBizId=cpsH5Coupon&bizId=656a6a27cc7746fd94b014c8f907a62a&mediumSrc1=656a6a27cc7746fd94b014c8f907a62a&scene=CPS_SELF_SRC&pageSrc1=CPS_SELF_OUT_SRC_H5_LINK&pageSrc2=656a6a27cc7746fd94b014c8f907a62a&pageSrc3=60e3be4df107403b9d5bda0b97ec7f4a&activityId=6&mediaPvId=dafkdsajffjafdfs&mediaUserId=10086&outActivityId=6&hoaePageV=8&p=1069986137059368960&poi_id_str=";
    private const string SecondTemplate = "https://offsiteact.meituan.com/web/hoae/collection_waimai_v6/index.html?pageSrc2=656a6a27cc7746fd94b014c8f907a62a&pageSrc1=CPS_SELF_OUT_SRC_H5_LINK&recallBizId=cpsH5Coupon&pageSrc3=c64c962ed5fd4bc784b9f6ad7d15ea90&gift_id=096c3386-4fd6-48f3-a07d-4799a5cff83c&scene=CPS_SELF_SRC&rootPvId=8d7c1814-d601-4a7f-801a-b74ae818d970&p=249e0a85d26d462dac5e090a3ebc4642&activityId=9&poi_id_str=";
    private static readonly Regex Link = new Regex(@"https?://[^\s<>""，。！；）”」]+", RegexOptions.IgnoreCase);
    private static readonly Regex Name = new Regex(@"「([^」]+)」");
    private static readonly Regex Poi = new Regex(@"^[A-Za-z0-9_-]{1,100}$");
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 4000000 };
    private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(12) };
    private static Task<CoreWebView2Environment> browserEnvironment;
    private readonly WebView2 view;
    private readonly string readCard;
    private Task initialization;
    private Request current;

    private sealed class Request
    {
        internal Store Store;
        internal decimal? Amount;
        internal DateTime? NoCouponAt, PageNoCouponAt;
        internal bool CouponRequestFailed, TargetMissingFromList, SawTargetCard;
        internal string Failure, FirstDisplayedStore;
        internal DateTime Started;
        internal readonly Dictionary<string, bool> CouponRequests = new Dictionary<string, bool>();
    }

    internal StoreLookup(WebView2 view)
    {
        this.view = view;
        readCard = ReadScript("read-coupon.js");
    }

    private static string ReadScript(string name)
    {
        using (var reader = new StreamReader(typeof(StoreLookup).Assembly.GetManifestResourceStream(name)))
            return reader.ReadToEnd();
    }

    internal static string ReadName(string input)
    {
        var name = Name.Match(input);
        return name.Success ? name.Groups[1].Value.Trim() : null;
    }

    internal static async Task<Store> ResolveAsync(string input, CancellationToken token)
    {
        var match = Link.Match(input);
        Uri uri;
        if (!match.Success || !Uri.TryCreate(match.Value, UriKind.Absolute, out uri) || !uri.IsDefaultPort || uri.UserInfo.Length != 0)
            throw new FormatException("请输入美团外卖门店链接或完整分享文案。");
        if (uri.Host == "dpurl.cn")
        {
            uri = new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
            using (var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token))
            {
                var location = response.Headers.Location;
                if ((int)response.StatusCode < 300 || (int)response.StatusCode >= 400 || location == null)
                    throw new FormatException("美团短链未返回门店地址，可能已失效。");
                uri = new Uri(uri, location);
            }
        }

        bool menu = uri.Host == "h5.waimai.meituan.com" && uri.AbsolutePath == "/waimai/mindex/menu";
        bool activity = uri.Host == ActivityHost && uri.AbsolutePath == ActivityPath;
        string id = HttpUtility.ParseQueryString(uri.Query)["poi_id_str"];
        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length != 0
            || (!menu && !activity) || id == null || !Poi.IsMatch(id))
            throw new FormatException("链接中没有可识别的美团外卖门店 ID。");
        return new Store
        {
            Id = id, Name = ReadName(input),
            CouponLink = CouponTemplate + Uri.EscapeDataString(id),
            SecondLink = SecondTemplate + Uri.EscapeDataString(id)
        };
    }

    private async Task InitializeAsync()
    {
        string profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WaimaiCoupon", "Browser");
        if (browserEnvironment == null || browserEnvironment.IsFaulted)
            browserEnvironment = CoreWebView2Environment.CreateAsync(null, profile);
        await view.EnsureCoreWebView2Async(await browserEnvironment);
        var browser = view.CoreWebView2;
        browser.Settings.AreDefaultScriptDialogsEnabled = false;
        browser.Settings.AreDefaultContextMenusEnabled = false;
        browser.Settings.AreDevToolsEnabled = false;
        browser.Settings.IsStatusBarEnabled = false;
        browser.NewWindowRequested += (sender, e) => e.Handled = true;
        browser.DownloadStarting += (sender, e) => { e.Cancel = true; e.Handled = true; };
        browser.BasicAuthenticationRequested += (sender, e) => e.Cancel = true;
        browser.PermissionRequested += (sender, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
            e.SavesInProfile = false;
        };
        browser.NavigationStarting += (sender, e) =>
        {
            Uri uri;
            e.Cancel = e.Uri != "about:blank" && (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri)
                || uri.Scheme != Uri.UriSchemeHttps || uri.Host != ActivityHost || !uri.IsDefaultPort);
        };
        browser.NavigationCompleted += (sender, e) =>
        {
            if (current != null && !e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                current.Failure = "美团活动页加载失败。";
        };
        browser.WebResourceResponseReceived += ObserveResponse;
        browser.GetDevToolsProtocolEventReceiver("Network.requestWillBeSent").DevToolsProtocolEventReceived += TrackCouponRequest;
        browser.GetDevToolsProtocolEventReceiver("Network.loadingFinished").DevToolsProtocolEventReceived += ReadCouponResponse;
        await browser.CallDevToolsProtocolMethodAsync("Network.enable", "{\"maxPostDataSize\":16384}");
    }

    internal async Task<LookupResult> QueryAsync(Store store, CancellationToken token)
    {
        if (initialization == null || initialization.IsFaulted)
            initialization = InitializeAsync();
        await WithCancellation(initialization, token);
        var request = new Request { Store = store, Started = DateTime.UtcNow };
        current = request;
        try
        {
            view.CoreWebView2.Navigate(store.CouponLink);
            while (DateTime.UtcNow - request.Started < TimeSpan.FromSeconds(22))
            {
                token.ThrowIfCancellationRequested();
                if (!string.IsNullOrEmpty(store.Name) && IsStorePage(view.Source, store.Id))
                {
                    var evaluate = view.CoreWebView2.ExecuteScriptAsync(
                        "(" + readCard + ")(" + Json.Serialize(store.Name) + "," + Json.Serialize(store.Id) + ")");
                    await WithCancellation(evaluate, token);
                    var card = Json.DeserializeObject(await evaluate) as Dictionary<string, object>;
                    if (card != null)
                    {
                        decimal amount;
                        if (Number(Get(card, "amount"), out amount) && amount > 0)
                            return Result(store, amount, false, null);
                        request.SawTargetCard |= Equals(Get(card, "matched"), true);
                        request.FirstDisplayedStore = Get(card, "firstName") as string ?? request.FirstDisplayedStore;
                    }
                    if (Equals(Get(card, "noCoupon"), true))
                        request.PageNoCouponAt = request.PageNoCouponAt ?? DateTime.UtcNow;
                    else
                        request.PageNoCouponAt = null;
                }

                var elapsed = DateTime.UtcNow - request.Started;
                // Give the activity page time to render its shop card before falling back to its response data.
                if (elapsed > TimeSpan.FromSeconds(7) && request.Amount.HasValue)
                    return Result(store, request.Amount, false, null);
                if (CanConfirmNoCoupon(request, DateTime.UtcNow))
                    return Result(store, null, true, null);
                if (elapsed > TimeSpan.FromSeconds(5) && request.Failure != null)
                    return Result(store, null, false, request.Failure);
                await Task.Delay(400, token);
            }
            if (request.Amount.HasValue)
                return Result(store, request.Amount, false, null);
            if (CanConfirmNoCoupon(request, DateTime.UtcNow))
                return Result(store, null, true, null);
            string detail = request.Failure;
            if (detail == null && request.SawTargetCard)
                detail = "美团页面已显示这家门店，但未展示可读取的专享商家券面额。";
            if (detail == null && request.FirstDisplayedStore != null)
                detail = "美团页面首家门店是「" + request.FirstDisplayedStore + "」，未找到目标门店的专享商家券。";
            if (detail == null && request.TargetMissingFromList)
                detail = "美团返回了活动券列表，但列表中没有这家门店，无法核实券额。";
            if (detail == null && request.CouponRequestFailed)
                detail = "美团券数据请求失败，请稍后重试。";
            return Result(store, null, false, detail ?? "未能从美团活动页核实该门店的商家券。");
        }
        finally
        {
            current = null;
            if (!view.IsDisposed) view.CoreWebView2.Stop();
        }
    }

    private static bool CanConfirmNoCoupon(Request request, DateTime now)
    {
        if (request.Amount.HasValue || request.Failure != null) return false;
        var confirmedAt = request.NoCouponAt ?? (request.CouponRequestFailed ? null : request.PageNoCouponAt);
        return confirmedAt.HasValue && now - confirmedAt.Value >= TimeSpan.FromMilliseconds(850);
    }

    private void ObserveResponse(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        var request = current;
        Uri uri;
        if (request == null || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.Host != ActivityHost || !uri.IsDefaultPort)
            return;
        if (uri.AbsolutePath != DataPath && uri.AbsolutePath != "/act/ge/getPageModuleInfo")
            return;
        int status = e.Response.StatusCode;
        if (status >= 400 && IsStorePage(view.Source, request.Store.Id))
        {
            if (uri.AbsolutePath == DataPath) request.CouponRequestFailed = true;
            else request.Failure = "美团活动页拒绝或未能返回数据（HTTP " + status + "）。";
        }
    }

    private void TrackCouponRequest(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        var request = current;
        if (request == null) return;
        var data = Parse(e.ParameterObjectAsJson);
        var sent = Get(data, "request") as Dictionary<string, object>;
        var id = Get(data, "requestId") as string;
        Uri document, url;
        if (id == null || !Uri.TryCreate(Get(data, "documentURL") as string, UriKind.Absolute, out document)
            || !IsStorePage(document, request.Store.Id)
            || !Uri.TryCreate(Get(sent, "url") as string, UriKind.Absolute, out url)
            || url.Scheme != Uri.UriSchemeHttps || url.Host != ActivityHost || !url.IsDefaultPort || url.AbsolutePath != DataPath)
            return;
        var body = Parse(Get(sent, "postData") as string);
        var medium = Get(body, "mediumParams") as Dictionary<string, object>;
        request.CouponRequests[id] = Equals(Get(body, "recallBizId"), "cpsEverDayCoupon")
            && Equals(Get(body, "pageNo"), 1) && Equals(Get(medium, "poi_id_str"), request.Store.Id);
    }

    private async void ReadCouponResponse(object sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
    {
        var request = current;
        if (request == null) return;
        var id = Get(Parse(e.ParameterObjectAsJson), "requestId") as string;
        bool targeted;
        if (id == null || !request.CouponRequests.TryGetValue(id, out targeted)) return;
        request.CouponRequests.Remove(id);
        try
        {
            var raw = Parse(await view.CoreWebView2.CallDevToolsProtocolMethodAsync("Network.getResponseBody",
                "{\"requestId\":" + Json.Serialize(id) + "}"));
            var body = Get(raw, "body") as string;
            if (body == null || current != request) return;
            if (Equals(Get(raw, "base64Encoded"), true))
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            var data = Parse(body);
            var infos = Get(data, "infos") as object[];
            if (!Equals(Get(data, "ret"), 0) || infos == null) return;
            bool matched = false, hasMerchantCoupon = false;
            foreach (var entry in infos)
            {
                var info = entry as Dictionary<string, object>;
                if (!Equals(Get(info, "poi_id_str"), request.Store.Id)) continue;
                matched = true;
                var name = Get(Get(info, "poiBaseInfo") as Dictionary<string, object>, "name") as string;
                if (!string.IsNullOrWhiteSpace(name)) request.Store.Name = name.Trim();
                var gift = Get(info, "giftInfo") as Dictionary<string, object>;
                if (!Equals(Get(gift, "type"), 1)) continue;
                hasMerchantCoupon = true;
                decimal fen;
                if (Number(Get(gift, "coupon_amount"), out fen) && fen > 0)
                    request.Amount = fen / 100m;
            }
            if (targeted && !hasMerchantCoupon && (matched || infos.Length == 0))
                request.NoCouponAt = DateTime.UtcNow;
            if (targeted && !matched && infos.Length > 0)
                request.TargetMissingFromList = true;
        }
        catch (Exception)
        {
            // If the browser could not retain a response, continue checking the rendered card.
        }
    }

    private static Dictionary<string, object> Parse(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return Json.DeserializeObject(json) as Dictionary<string, object>; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsStorePage(Uri page, string id)
    {
        return page != null && page.Scheme == Uri.UriSchemeHttps && page.Host == ActivityHost
            && page.AbsolutePath == ActivityPath && HttpUtility.ParseQueryString(page.Query)["poi_id_str"] == id;
    }

    private static object Get(Dictionary<string, object> values, string key)
    {
        object value;
        return values != null && values.TryGetValue(key, out value) ? value : null;
    }

    private static bool Number(object value, out decimal number)
    {
        return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
            NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out number);
    }

    private static async Task WithCancellation(Task task, CancellationToken token)
    {
        var canceled = new TaskCompletionSource<bool>();
        using (token.Register(() => canceled.TrySetResult(true)))
        {
            if (task != await Task.WhenAny(task, canceled.Task)) token.ThrowIfCancellationRequested();
            await task;
        }
    }

    private static LookupResult Result(Store store, decimal? amount, bool noCoupon, string detail)
    {
        return new LookupResult
        {
            Name = store.Name, Amount = amount, NoCoupon = noCoupon, Detail = detail,
            CouponLink = store.CouponLink, SecondLink = store.SecondLink
        };
    }

}
