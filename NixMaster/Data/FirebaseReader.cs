using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NixMaster.Core;
using NixMaster.Models;

namespace NixMaster.Data
{
    /// <summary>
    /// Reads data from Firebase RTDB and returns combined records.
    /// </summary>
    public class FirebaseReader
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public bool IsOnline { get; private set; } = true;

        private string BaseUrl  => AppState.Settings.FirebaseUrl.TrimEnd('/');
        private string NodePath => AppState.Settings.NodePath.Trim('/');

        // ─── Caching ─────────────────────────────────────────────────────────────

        private static List<DispatchRecord>? _cachedDispatches;
        private static DateTime _dispatchesCacheTime = DateTime.MinValue;

        private static Dictionary<string, SubAssyRecord>? _cachedIrRecords;
        private static DateTime _irCacheTime = DateTime.MinValue;

        private static Dictionary<string, SubAssyRecord>? _cachedIrIndRecords;
        private static DateTime _irIndCacheTime = DateTime.MinValue;

        private static Dictionary<string, SubAssyRecord>? _cachedCamRecords;
        private static DateTime _camCacheTime = DateTime.MinValue;

        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private async Task<List<DispatchRecord>> GetDispatchesCachedAsync()
        {
            if (_cachedDispatches != null && DateTime.Now - _dispatchesCacheTime < CacheDuration)
                return _cachedDispatches;
            
            try {
                var json = await _http.GetStringAsync($"{BaseUrl}/Dispatches.json");
                _cachedDispatches = ParseDispatches(json);
                _dispatchesCacheTime = DateTime.Now;
            } catch {
                _cachedDispatches ??= new List<DispatchRecord>();
            }
            return _cachedDispatches;
        }

        private async Task<Dictionary<string, SubAssyRecord>> GetSubAssyCachedAsync(string endpoint, string cacheKey)
        {
            if (cacheKey == "IR" && _cachedIrRecords != null && DateTime.Now - _irCacheTime < CacheDuration) return _cachedIrRecords;
            if (cacheKey == "IR_IND" && _cachedIrIndRecords != null && DateTime.Now - _irIndCacheTime < CacheDuration) return _cachedIrIndRecords;
            if (cacheKey == "CAM" && _cachedCamRecords != null && DateTime.Now - _camCacheTime < CacheDuration) return _cachedCamRecords;

            Dictionary<string, SubAssyRecord>? result = null;
            try {
                var json = await _http.GetStringAsync($"{BaseUrl}/{endpoint}.json");
                result = ParseSubAssy(json);
                
                if (cacheKey == "IR") { _cachedIrRecords = result; _irCacheTime = DateTime.Now; }
                else if (cacheKey == "IR_IND") { _cachedIrIndRecords = result; _irIndCacheTime = DateTime.Now; }
                else if (cacheKey == "CAM") { _cachedCamRecords = result; _camCacheTime = DateTime.Now; }
            } catch {
                result = new Dictionary<string, SubAssyRecord>(StringComparer.OrdinalIgnoreCase);
                if (cacheKey == "IR") _cachedIrRecords ??= result;
                else if (cacheKey == "IR_IND") _cachedIrIndRecords ??= result;
                else if (cacheKey == "CAM") _cachedCamRecords ??= result;
            }
            return result ?? new Dictionary<string, SubAssyRecord>(StringComparer.OrdinalIgnoreCase);
        }

        private Task<Dictionary<string, SubAssyRecord>> GetIrRecordsCachedAsync() => GetSubAssyCachedAsync("IR%20PCBA%20SUB%20ASSy", "IR");
        private Task<Dictionary<string, SubAssyRecord>> GetIrIndRecordsCachedAsync() => GetSubAssyCachedAsync("IR%20INDICATION%20PCBA%20SUB%20ASSY", "IR_IND");
        private Task<Dictionary<string, SubAssyRecord>> GetCamRecordsCachedAsync() => GetSubAssyCachedAsync("Camera%20sub%20assy", "CAM");

        // ─── Fetch All ───────────────────────────────────────────────────────────

        /// <summary>
        /// Fetches only records whose AssemblyApp/Timestamp falls between fromDate and toDate.
        /// Uses Firebase orderBy + startAt + endAt REST query params to minimise data download.
        /// </summary>
        public async Task<(List<CombinedRecord> records, string error)> FetchRangeAsync(DateTime fromDate, DateTime toDate)
        {
            try
            {
                string startAt = fromDate.ToString("yyyy-MM-dd");
                string endAt   = toDate.AddDays(1).ToString("yyyy-MM-dd"); // exclusive upper bound

                // Firebase orderBy query — fetches only records in this date range
                // NOTE: This requires .indexOn rule on Firebase. If it fails or returns empty,
                // we fall back to full fetch + local date filter.
                string orderedUrl = $"{BaseUrl}/{NodePath}.json" +
                             $"?orderBy=%22AssemblyApp%2FTimestamp%22" +
                             $"&startAt=%22{startAt}%22" +
                             $"&endAt=%22{endAt}%22";

                var tDisp  = GetDispatchesCachedAsync();
                var tIr    = GetIrRecordsCachedAsync();
                var tIrInd = GetIrIndRecordsCachedAsync();
                var tCam   = GetCamRecordsCachedAsync();

                string json = "null";
                bool usedFallback = false;

                try
                {
                    json = await _http.GetStringAsync(orderedUrl);
                }
                catch { json = "null"; }

                // If Firebase orderBy returns null/empty (missing .indexOn rule), fall back to full fetch
                if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "{}")
                {
                    usedFallback = true;
                    try { json = await _http.GetStringAsync($"{BaseUrl}/{NodePath}.json"); } catch { json = "null"; }
                }

                await Task.WhenAll(tDisp, tIr, tIrInd, tCam);

                if (string.IsNullOrWhiteSpace(json) || json == "null")
                    return (new List<CombinedRecord>(), "");

                var dispatches   = tDisp.Result;
                var irRecords    = tIr.Result;
                var irIndRecords = tIrInd.Result;
                var camRecords   = tCam.Result;

                var root = JObject.Parse(json);
                var list = new List<CombinedRecord>();

                foreach (var prop in root.Properties())
                {
                    string macId  = prop.Name;
                    var macNode   = prop.Value as JObject;
                    if (macNode == null) continue;

                    var rec = ParseNodeToRecord(macId, macNode);

                    // When using fallback full-fetch, apply local date filter
                    if (usedFallback)
                    {
                        string? ts = rec.Assembly?.Timestamp;
                        if (string.IsNullOrEmpty(ts)) continue;
                        // ts format: "yyyy-MM-dd ..." — compare date portion only
                        string tsDate = ts.Length >= 10 ? ts.Substring(0, 10) : ts;
                        if (string.Compare(tsDate, startAt, StringComparison.Ordinal) < 0) continue;
                        if (string.Compare(tsDate, endAt,   StringComparison.Ordinal) >= 0) continue;
                    }

                    if (rec.Packing != null && !string.IsNullOrWhiteSpace(rec.Packing.BoxNo))
                    {
                        string bnoStr = rec.Packing.BoxNo.Trim();
                        var dispatch = dispatches.FirstOrDefault(d =>
                            string.Compare(bnoStr, d.FromBoxNo, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            string.Compare(bnoStr, d.ToBoxNo,   StringComparison.OrdinalIgnoreCase) <= 0);
                        if (dispatch != null)
                        {
                            rec.Dispatch = dispatch;
                        }
                    }

                    if (rec.Assembly?.Parts != null)
                    {
                        foreach (var kv in rec.Assembly.Parts)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                            string v = kv.Value.Trim();
                            if (v.IndexOf("CAM", StringComparison.OrdinalIgnoreCase) >= 0)
                            { if (camRecords.TryGetValue(v, out var cr)) rec.CamSubAssy = cr; }
                            else if (v.IndexOf("IRL", StringComparison.OrdinalIgnoreCase) >= 0)
                            { if (irRecords.TryGetValue(v, out var ir)) rec.IrSubAssy = ir; }
                            else
                            {
                                if (irRecords.TryGetValue(v,    out var irR))  rec.IrSubAssy = irR;
                                if (irIndRecords.TryGetValue(v, out var irI))  rec.IrIndicationSubAssy = irI;
                                if (camRecords.TryGetValue(v,   out var camR)) rec.CamSubAssy = camR;
                            }
                        }
                    }

                    list.Add(rec);
                }

                list.Sort((a, b) => string.Compare(
                    b.Assembly?.Timestamp ?? "",
                    a.Assembly?.Timestamp ?? "",
                    StringComparison.OrdinalIgnoreCase));

                IsOnline = true;
                return (list, "");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (new List<CombinedRecord>(), ex.Message);
            }
        }

        public async Task<(List<CombinedRecord> records, string error)> FetchAllAsync()
        {
            try
            {
                var tMain = _http.GetStringAsync($"{BaseUrl}/{NodePath}.json");
                var tDisp = GetDispatchesCachedAsync();
                var tIr   = GetIrRecordsCachedAsync();
                var tIrInd= GetIrIndRecordsCachedAsync();
                var tCam  = GetCamRecordsCachedAsync();

                // Wait for all requests
                await Task.WhenAll(
                    SafeTask(tMain), 
                    tDisp, 
                    tIr, 
                    tIrInd,
                    tCam
                );

                string json = tMain.Status == TaskStatus.RanToCompletion ? tMain.Result : "null";
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                    return (new List<CombinedRecord>(), "");

                var dispatches   = tDisp.Result;
                var irRecords    = tIr.Result;
                var irIndRecords = tIrInd.Result;
                var camRecords   = tCam.Result;

                var root = JObject.Parse(json);
                var list = new List<CombinedRecord>();

                foreach (var prop in root.Properties())
                {
                    string macId = prop.Name;
                    var macNode  = prop.Value as JObject;
                    if (macNode == null) continue;

                    var rec = ParseNodeToRecord(macId, macNode);

                    // Join Dispatch Data
                    if (rec.Packing != null && !string.IsNullOrWhiteSpace(rec.Packing.BoxNo))
                    {
                        string bnoStr = rec.Packing.BoxNo.Trim();
                        var dispatch = dispatches.FirstOrDefault(d => 
                            string.Compare(bnoStr, d.FromBoxNo, StringComparison.OrdinalIgnoreCase) >= 0 && 
                            string.Compare(bnoStr, d.ToBoxNo, StringComparison.OrdinalIgnoreCase) <= 0);
                        if (dispatch != null)
                        {
                            rec.Dispatch = dispatch;
                        }
                    }

                    // Join Sub Assy Data
                    if (rec.Assembly != null && rec.Assembly.Parts != null)
                    {
                        foreach (var kv in rec.Assembly.Parts)
                        {
                            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                            string trimmedVal = kv.Value.Trim();

                            // Camera PCBA match — only for keys that are camera PCBA QR (SGS...CAM...)
                            if (trimmedVal.IndexOf("CAM", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (camRecords.TryGetValue(trimmedVal, out var camRec))
                                    rec.CamSubAssy = camRec;
                            }
                            // IR INDICATION match
                            else if (trimmedVal.IndexOf("IND", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (irIndRecords.TryGetValue(trimmedVal, out var irIndRec))
                                    rec.IrIndicationSubAssy = irIndRec;
                            }
                            // IR LED PCBA match
                            else if (trimmedVal.IndexOf("IRL", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                if (irRecords.TryGetValue(trimmedVal, out var irRec))
                                    rec.IrSubAssy = irRec;
                            }
                            else
                            {
                                // Generic fallback — try all
                                if (irRecords.TryGetValue(trimmedVal, out var irRec))
                                    rec.IrSubAssy = irRec;
                                if (irIndRecords.TryGetValue(trimmedVal, out var irIndRec2))
                                    rec.IrIndicationSubAssy = irIndRec2;
                                if (camRecords.TryGetValue(trimmedVal, out var camRec2))
                                    rec.CamSubAssy = camRec2;
                            }
                        }
                    }

                    list.Add(rec);
                }

                // Sort newest first by assembly timestamp
                list.Sort((a, b) => string.Compare(
                    b.Assembly?.Timestamp ?? "",
                    a.Assembly?.Timestamp ?? "",
                    StringComparison.OrdinalIgnoreCase));

                IsOnline = true;
                return (list, "");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (new List<CombinedRecord>(), ex.Message);
            }
        }

        // ─── Fetch Single MAC ────────────────────────────────────────────────────

        public async Task<(CombinedRecord? record, string error)> FetchMacAsync(string macId)
        {
            try
            {
                string safeKey = SanitizeKey(macId);
                string url     = $"{BaseUrl}/{NodePath}/{Uri.EscapeDataString(safeKey)}.json";
                
                var tMain = _http.GetStringAsync(url);
                var tDisp = GetDispatchesCachedAsync();
                var tIr   = GetIrRecordsCachedAsync();
                var tIrInd= GetIrIndRecordsCachedAsync();
                var tCam  = GetCamRecordsCachedAsync();

                await Task.WhenAll(SafeTask(tMain), tDisp, tIr, tIrInd, tCam);

                string json = tMain.Status == TaskStatus.RanToCompletion ? tMain.Result : "null";
                if (string.IsNullOrWhiteSpace(json) || json == "null")
                    return (null, "NOT_FOUND");

                var dispatches   = tDisp.Result;
                var irRecords    = tIr.Result;
                var irIndRecords = tIrInd.Result;
                var camRecords   = tCam.Result;

                var node = JObject.Parse(json);
                var rec = ParseNodeToRecord(macId, node);

                // Join Dispatch Data
                if (rec.Packing != null && !string.IsNullOrWhiteSpace(rec.Packing.BoxNo))
                {
                    string bnoStr = rec.Packing.BoxNo.Trim();
                    var dispatch = dispatches.FirstOrDefault(d => 
                        string.Compare(bnoStr, d.FromBoxNo, StringComparison.OrdinalIgnoreCase) >= 0 && 
                        string.Compare(bnoStr, d.ToBoxNo, StringComparison.OrdinalIgnoreCase) <= 0);
                    if (dispatch != null)
                        rec.Dispatch = dispatch;
                }

                // Join Sub Assy Data
                if (rec.Assembly != null && rec.Assembly.Parts != null)
                {
                    foreach (var kv in rec.Assembly.Parts)
                    {
                        if (string.IsNullOrWhiteSpace(kv.Value)) continue;
                        string trimmedVal = kv.Value.Trim();

                        if (trimmedVal.IndexOf("CAM", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (camRecords.TryGetValue(trimmedVal, out var camRec))
                                rec.CamSubAssy = camRec;
                        }
                        else if (trimmedVal.IndexOf("IND", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (irIndRecords.TryGetValue(trimmedVal, out var irIndRec))
                                rec.IrIndicationSubAssy = irIndRec;
                        }
                        else if (trimmedVal.IndexOf("IRL", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (irRecords.TryGetValue(trimmedVal, out var irRec))
                                rec.IrSubAssy = irRec;
                        }
                        else
                        {
                            if (irRecords.TryGetValue(trimmedVal, out var irRec))
                                rec.IrSubAssy = irRec;
                            if (irIndRecords.TryGetValue(trimmedVal, out var irIndRec2))
                                rec.IrIndicationSubAssy = irIndRec2;
                            if (camRecords.TryGetValue(trimmedVal, out var camRec2))
                                rec.CamSubAssy = camRec2;
                        }
                    }
                }

                IsOnline = true;
                return (rec, "");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (null, ex.Message);
            }
        }

        // ─── Fetch Raw Sub Assy Data ─────────────────────────────────────────────
        public async Task<(Dictionary<string, List<SubAssyRecord>> Data, string Error)> FetchRawSubAssyStatsAsync()
        {
            try
            {
                var dict = new Dictionary<string, List<SubAssyRecord>>(StringComparer.OrdinalIgnoreCase);

                foreach (var prod in AppState.Settings.SubAssyProducts)
                {
                    // Since SubAssy are often fetched here too, maybe use cache if it matches
                    string endpoint = Uri.EscapeDataString(prod);
                    if (endpoint.IndexOf("IR", StringComparison.OrdinalIgnoreCase) >= 0 && endpoint.IndexOf("IND", StringComparison.OrdinalIgnoreCase) < 0) {
                        dict[prod] = (await GetIrRecordsCachedAsync()).Values.ToList();
                    } else if (endpoint.IndexOf("IND", StringComparison.OrdinalIgnoreCase) >= 0) {
                        dict[prod] = (await GetIrIndRecordsCachedAsync()).Values.ToList();
                    } else if (endpoint.IndexOf("Cam", StringComparison.OrdinalIgnoreCase) >= 0) {
                        dict[prod] = (await GetCamRecordsCachedAsync()).Values.ToList();
                    } else {
                        // Fallback
                        string json = await _http.GetStringAsync($"{BaseUrl}/{endpoint}.json");
                        dict[prod] = ParseSubAssy(json).Values.ToList();
                    }
                }

                IsOnline = true;
                return (dict, "");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (new Dictionary<string, List<SubAssyRecord>>(), ex.Message);
            }
        }

        // ─── Test Connection ─────────────────────────────────────────────────────

        public async Task<(bool ok, string msg)> TestConnectionAsync()
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                var res = await http.GetAsync($"{BaseUrl}/.json?shallow=true");
                if (res.IsSuccessStatusCode)
                {
                    IsOnline = true;
                    return (true, $"✔ Connected  (HTTP {(int)res.StatusCode})");
                }
                return (false, $"✗ HTTP {(int)res.StatusCode} — {res.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (false, $"✗ {ex.Message}");
            }
        }

        // ─── Fetch Line Status Data ──────────────────────────────────────────────

        public async Task<(List<ProductLineStats> Stats, string Error)> FetchLineStatusDataAsync()
        {
            try
            {
                var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
                var monthStr = DateTime.Now.ToString("yyyy-MM");
                var cutoff15 = DateTime.Now.AddMinutes(-15);

                var tasks = AppState.Settings.Lines.Select(async line =>
                {
                    string lBase = line.FirebaseUrl.TrimEnd('/');
                    string lNode = line.NodePath.Trim('/');

                    var tDisp    = _http.GetStringAsync($"{lBase}/Dispatches.json");
                    var tTarget  = _http.GetStringAsync($"{lBase}/MonthlyTarget.json");
                    var tDaily   = _http.GetStringAsync($"{lBase}/DailyPlan.json");
                    var tMetrics = _http.GetStringAsync($"{lBase}/Metrics.json");

                    await Task.WhenAll(SafeTask(tDisp), SafeTask(tTarget), SafeTask(tDaily), SafeTask(tMetrics));

                    string dispJson = tDisp.Status == TaskStatus.RanToCompletion ? tDisp.Result : "null";
                    var dispatches = ParseDispatches(dispJson);

                    int todayAssembled = 0, monthAssembled = 0, totalAssembly = 0;
                    int todayTested = 0, todayDefects = 0, totalDispatchedUnits = 0;
                    bool isRunning = false;

                    string metricsJson = tMetrics.Status == TaskStatus.RanToCompletion ? tMetrics.Result : "null";

                    if (string.IsNullOrWhiteSpace(metricsJson) || metricsJson == "null")
                    {
                        string mainJson = "null";
                        try { mainJson = await _http.GetStringAsync($"{lBase}/{lNode}.json"); } catch { }
                        var mainRecords = ParseMainRecords(mainJson);
                        string lastTimestamp = "";

                        foreach (var r in mainRecords)
                        {
                            if (r.Assembly != null && !string.IsNullOrEmpty(r.Assembly.Timestamp))
                            {
                                totalAssembly++;
                                if (r.Assembly.Timestamp.StartsWith(todayStr)) todayAssembled++;
                                if (r.Assembly.Timestamp.StartsWith(monthStr)) monthAssembled++;
                                
                                if (DateTime.TryParse(r.Assembly.Timestamp, out var dt))
                                {
                                    if (dt >= cutoff15) isRunning = true;
                                    if (string.Compare(r.Assembly.Timestamp, lastTimestamp, StringComparison.Ordinal) > 0)
                                        lastTimestamp = r.Assembly.Timestamp;
                                }
                            }

                            if (r.Testing != null && !string.IsNullOrEmpty(r.Testing.TestedAt))
                            {
                                if (r.Testing.TestedAt.StartsWith(todayStr))
                                {
                                    todayTested++;
                                    if (r.Testing.Status?.Equals("NG", StringComparison.OrdinalIgnoreCase) == true)
                                        todayDefects++;
                                }
                            }

                            if (r.Packing != null && !string.IsNullOrWhiteSpace(r.Packing.BoxNo))
                            {
                                string bnoStr = r.Packing.BoxNo.Trim();
                                bool isDispatched = dispatches.Any(d => 
                                    string.Compare(bnoStr, d.FromBoxNo, StringComparison.OrdinalIgnoreCase) >= 0 && 
                                    string.Compare(bnoStr, d.ToBoxNo, StringComparison.OrdinalIgnoreCase) <= 0);
                                if (isDispatched)
                                    totalDispatchedUnits++;
                            }
                        }

                        var metricsObj = new {
                            AllTime = new { TotalAssembled = totalAssembly, TotalDispatched = totalDispatchedUnits },
                            LastAssemblyTimestamp = lastTimestamp
                        };
                        string body = Newtonsoft.Json.JsonConvert.SerializeObject(metricsObj);
                        var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                        await _http.PutAsync($"{lBase}/Metrics.json", content);
                        
                        var mContent = new StringContent($"{{\"MonthAssembled\": {monthAssembled}}}", System.Text.Encoding.UTF8, "application/json");
                        await _http.PutAsync($"{lBase}/Metrics/{monthStr}.json", mContent);

                        var tContent = new StringContent($"{{\"TodayAssembled\": {todayAssembled}, \"TodayTested\": {todayTested}, \"TodayDefects\": {todayDefects}}}", System.Text.Encoding.UTF8, "application/json");
                        await _http.PutAsync($"{lBase}/Metrics/{todayStr}.json", tContent);
                    }
                    else
                    {
                        try
                        {
                            var root = JObject.Parse(metricsJson);
                            if (root["AllTime"] != null)
                            {
                                totalAssembly = root["AllTime"]["TotalAssembled"]?.Value<int>() ?? 0;
                                totalDispatchedUnits = root["AllTime"]["TotalDispatched"]?.Value<int>() ?? 0;
                            }
                            if (root[monthStr] != null)
                            {
                                monthAssembled = root[monthStr]["MonthAssembled"]?.Value<int>() ?? 0;
                            }
                            if (root[todayStr] != null)
                            {
                                todayAssembled = root[todayStr]["TodayAssembled"]?.Value<int>() ?? 0;
                                todayTested = root[todayStr]["TodayTested"]?.Value<int>() ?? 0;
                                todayDefects = root[todayStr]["TodayDefects"]?.Value<int>() ?? 0;
                            }
                            
                            string lastTs = root["LastAssemblyTimestamp"]?.ToString() ?? "";
                            if (DateTime.TryParse(lastTs, out var dt) && dt >= cutoff15)
                                isRunning = true;
                        }
                        catch { }
                    }

                    int inventory = Math.Max(0, totalAssembly - totalDispatchedUnits);

                    string targetJson = tTarget.Status == TaskStatus.RanToCompletion ? tTarget.Result : "null";
                    int target = 0;
                    if (!string.IsNullOrWhiteSpace(targetJson) && targetJson != "null")
                        int.TryParse(targetJson, out target);

                    string dailyJson = tDaily.Status == TaskStatus.RanToCompletion ? tDaily.Result : "null";
                    var dailyPlans = ParseDailyPlan(dailyJson);

                    int todayTarget = 0;
                    int todayTestingTarget = 0;
                    if (dailyPlans.TryGetValue(todayStr, out var todaysPlan))
                    {
                        todayTarget = todaysPlan.MainTarget;
                        todayTestingTarget = todaysPlan.TestingTarget;
                    }

                    return new ProductLineStats
                    {
                        ProductName = line.LineName,
                        TodayAssembled = todayAssembled,
                        TodayTarget = todayTarget,
                        CurrentMonthAssembled = monthAssembled,
                        MonthlyTarget = target,
                        TodayTested = todayTested,
                        TodayTestingTarget = todayTestingTarget,
                        TodayDefects = todayDefects,
                        Inventory = inventory,
                        IsRunning = isRunning
                    };
                });

                var statsArray = await Task.WhenAll(tasks);
                var stats = statsArray.ToList();

                IsOnline = true;
                return (stats, "");
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (new List<ProductLineStats>(), ex.Message);
            }
        }

        // ─── Recalculate Metrics ─────────────────────────────────────────────────

        /// <summary>
        /// Full scan of all Firebase records — recalculates and overwrites the Metrics node.
        /// Returns a progress/summary string.
        /// </summary>
        public async Task<(bool ok, string summary)> RecalculateMetricsAsync(
            IProgress<string>? progress = null)
        {
            try
            {
                progress?.Report("⏳  Fetching main assembly records…");

                string mainJson = "null";
                try { mainJson = await _http.GetStringAsync($"{BaseUrl}/{NodePath}.json"); }
                catch (Exception ex) { return (false, $"Failed to fetch records: {ex.Message}"); }

                if (string.IsNullOrWhiteSpace(mainJson) || mainJson == "null")
                    return (false, "No records found in Firebase.");

                // ── Fetch sub-assy nodes in parallel ──────────────────────────────
                progress?.Report("⏳  Fetching sub-assembly records (IR / IR-IND / Camera)…");

                string irJson = "null", irIndJson = "null", camJson = "null";
                try { irJson    = await _http.GetStringAsync($"{BaseUrl}/IR%20PCBA%20SUB%20ASSy.json"); }    catch { }
                try { irIndJson = await _http.GetStringAsync($"{BaseUrl}/IR%20INDICATION%20PCBA%20SUB%20ASSY.json"); } catch { }
                try { camJson   = await _http.GetStringAsync($"{BaseUrl}/Camera%20sub%20assy.json"); }       catch { }

                var dispatches = await GetDispatchesCachedAsync();

                progress?.Report("⚙  Parsing and calculating all metrics…");

                var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
                var monthStr = DateTime.Now.ToString("yyyy-MM");

                // ── Bucket types ──────────────────────────────────────────────────
                // dayBucket: per-day counts for assembly, testing, packing
                var dayBuckets = new Dictionary<string, (int Assembled, int Tested, int TestedOk, int Defects, int Packed)>();
                // monthBuckets: per-month assembled count
                var monthBuckets = new Dictionary<string, int>();
                // sub-assy per-day
                var irDayBuckets    = new Dictionary<string, int>();
                var irIndDayBuckets = new Dictionary<string, int>();
                var camDayBuckets   = new Dictionary<string, int>();

                int totalAssembled = 0, totalDispatched = 0, totalTested = 0,
                    totalTestedOk = 0, totalDefects = 0, totalPacked = 0;
                string lastTimestamp = "";

                // ── Parse main records (assembly + testing + packing) ─────────────
                var root = JObject.Parse(mainJson);
                int parsed = 0;

                foreach (var prop in root.Properties())
                {
                    if (prop.Value is not JObject node) continue;
                    parsed++;

                    // Assembly
                    var asmToken = node["AssemblyApp"];
                    AssemblyRecord? assembly = null;
                    if (asmToken != null && asmToken.Type != JTokenType.Null)
                        assembly = asmToken.ToObject<AssemblyRecord>();

                    if (assembly != null && !string.IsNullOrEmpty(assembly.Timestamp))
                    {
                        totalAssembled++;
                        string ts = assembly.Timestamp;
                        string dayKey = ts.Length >= 10 ? ts.Substring(0, 10) : ts;
                        string monKey = ts.Length >=  7 ? ts.Substring(0,  7) : ts;

                        if (!dayBuckets.ContainsKey(dayKey)) dayBuckets[dayKey] = (0, 0, 0, 0, 0);
                        var d = dayBuckets[dayKey];
                        dayBuckets[dayKey] = (d.Assembled + 1, d.Tested, d.TestedOk, d.Defects, d.Packed);

                        if (!monthBuckets.ContainsKey(monKey)) monthBuckets[monKey] = 0;
                        monthBuckets[monKey]++;

                        if (string.Compare(ts, lastTimestamp, StringComparison.Ordinal) > 0)
                            lastTimestamp = ts;
                    }

                    // Testing
                    var testToken = node["TestingApp"];
                    TestingRecord? testing = null;
                    if (testToken != null && testToken.Type != JTokenType.Null)
                        testing = testToken.ToObject<TestingRecord>();

                    if (testing != null && !string.IsNullOrEmpty(testing.TestedAt))
                    {
                        bool isNg = testing.Status?.Equals("NG", StringComparison.OrdinalIgnoreCase) == true;
                        string dayKey = testing.TestedAt.Length >= 10 ? testing.TestedAt.Substring(0, 10) : testing.TestedAt;
                        if (!dayBuckets.ContainsKey(dayKey)) dayBuckets[dayKey] = (0, 0, 0, 0, 0);
                        var d = dayBuckets[dayKey];
                        dayBuckets[dayKey] = (d.Assembled, d.Tested + 1, d.TestedOk + (isNg ? 0 : 1), d.Defects + (isNg ? 1 : 0), d.Packed);
                        totalTested++;
                        if (isNg) totalDefects++; else totalTestedOk++;
                    }

                    // Packing
                    var packToken = node["PackingApp"];
                    PackingRecord? packing = null;
                    if (packToken != null && packToken.Type != JTokenType.Null)
                        packing = packToken.ToObject<PackingRecord>();

                    if (packing != null && !string.IsNullOrEmpty(packing.PackedAt))
                    {
                        string dayKey = packing.PackedAt.Length >= 10 ? packing.PackedAt.Substring(0, 10) : packing.PackedAt;
                        if (!dayBuckets.ContainsKey(dayKey)) dayBuckets[dayKey] = (0, 0, 0, 0, 0);
                        var d = dayBuckets[dayKey];
                        dayBuckets[dayKey] = (d.Assembled, d.Tested, d.TestedOk, d.Defects, d.Packed + 1);
                        totalPacked++;
                    }

                    // Dispatch
                    if (packing != null && !string.IsNullOrWhiteSpace(packing.BoxNo))
                    {
                        string bnoStr = packing.BoxNo.Trim();
                        bool isDisp = dispatches.Any(d =>
                            string.Compare(bnoStr, d.FromBoxNo, StringComparison.OrdinalIgnoreCase) >= 0 &&
                            string.Compare(bnoStr, d.ToBoxNo,   StringComparison.OrdinalIgnoreCase) <= 0);
                        if (isDisp) totalDispatched++;
                    }
                }

                // ── Parse sub-assy nodes ──────────────────────────────────────────
                int totalIr = 0, totalIrInd = 0, totalCam = 0;

                void ParseSubAssyNode(string json, Dictionary<string, int> dayBkt, ref int total)
                {
                    if (string.IsNullOrWhiteSpace(json) || json == "null") return;
                    try
                    {
                        var node2 = JObject.Parse(json);
                        foreach (var prop2 in node2.Properties())
                        {
                            var rec = prop2.Value?.ToObject<SubAssyRecord>();
                            if (rec == null || string.IsNullOrEmpty(rec.ScannedAt)) continue;
                            string dayKey = rec.ScannedAt.Length >= 10 ? rec.ScannedAt.Substring(0, 10) : rec.ScannedAt;
                            if (!dayBkt.ContainsKey(dayKey)) dayBkt[dayKey] = 0;
                            dayBkt[dayKey]++;
                            total++;
                        }
                    }
                    catch { }
                }

                ParseSubAssyNode(irJson,    irDayBuckets,    ref totalIr);
                ParseSubAssyNode(irIndJson, irIndDayBuckets, ref totalIrInd);
                ParseSubAssyNode(camJson,   camDayBuckets,   ref totalCam);

                progress?.Report($"📊  Processed {parsed} assembly + {totalIr + totalIrInd + totalCam} sub-assy records. Writing to Firebase…");

                // ── Build final Metrics object ────────────────────────────────────
                var metricsDict = new Dictionary<string, object>
                {
                    ["AllTime"] = new
                    {
                        TotalAssembled  = totalAssembled,
                        TotalDispatched = totalDispatched,
                        TotalTested     = totalTested,
                        TotalTestedOk   = totalTestedOk,
                        TotalDefects    = totalDefects,
                        TotalPacked     = totalPacked,
                        TotalIrScanned    = totalIr,
                        TotalIrIndScanned = totalIrInd,
                        TotalCamScanned   = totalCam
                    },
                    ["LastAssemblyTimestamp"] = lastTimestamp
                };

                // Per-month
                foreach (var kv in monthBuckets)
                    metricsDict[kv.Key] = new { MonthAssembled = kv.Value };

                // Per-day (assembly + testing + packing) — wins over month key (different format)
                foreach (var kv in dayBuckets)
                {
                    var ir    = irDayBuckets.TryGetValue(kv.Key, out var irv) ? irv : 0;
                    var irInd = irIndDayBuckets.TryGetValue(kv.Key, out var irIndv) ? irIndv : 0;
                    var cam   = camDayBuckets.TryGetValue(kv.Key, out var camv) ? camv : 0;

                    metricsDict[kv.Key] = new
                    {
                        TodayAssembled  = kv.Value.Assembled,
                        TodayTested     = kv.Value.Tested,
                        TodayTestedOk   = kv.Value.TestedOk,
                        TodayDefects    = kv.Value.Defects,
                        TodayPacked     = kv.Value.Packed,
                        TodayIrScanned    = ir,
                        TodayIrIndScanned = irInd,
                        TodayCamScanned   = cam
                    };
                }

                // Sub-assy days that have no assembly activity (edge case)
                foreach (var kv in irDayBuckets)
                    if (!metricsDict.ContainsKey(kv.Key))
                        metricsDict[kv.Key] = new { TodayIrScanned = kv.Value };
                foreach (var kv in irIndDayBuckets)
                    if (!metricsDict.ContainsKey(kv.Key))
                        metricsDict[kv.Key] = new { TodayIrIndScanned = kv.Value };
                foreach (var kv in camDayBuckets)
                    if (!metricsDict.ContainsKey(kv.Key))
                        metricsDict[kv.Key] = new { TodayCamScanned = kv.Value };

                string body = JsonConvert.SerializeObject(metricsDict);
                var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var res = await _http.PutAsync($"{BaseUrl}/Metrics.json", content);

                if (!res.IsSuccessStatusCode)
                    return (false, $"Firebase write failed: HTTP {(int)res.StatusCode}");

                _cachedDispatches = null; // force fresh next load

                string summary =
                    $"✅ Metrics recalculated successfully!\n\n" +
                    $"   Records scanned    : {parsed}\n" +
                    $"   Total Assembled    : {totalAssembled}\n" +
                    $"   Total Tested       : {totalTested}  (OK: {totalTestedOk}  NG: {totalDefects})\n" +
                    $"   Total Packed       : {totalPacked}\n" +
                    $"   Total Dispatched   : {totalDispatched}\n" +
                    $"   Current Inventory  : {Math.Max(0, totalAssembled - totalDispatched)}\n" +
                    $"   IR PCBA Scanned    : {totalIr}\n" +
                    $"   IR IND Scanned     : {totalIrInd}\n" +
                    $"   Camera Scanned     : {totalCam}\n" +
                    $"   This Month Asm     : {(monthBuckets.ContainsKey(monthStr) ? monthBuckets[monthStr] : 0)}\n" +
                    $"   Today Assembled    : {(dayBuckets.ContainsKey(todayStr) ? dayBuckets[todayStr].Assembled : 0)}";

                IsOnline = true;
                return (true, summary);
            }
            catch (Exception ex)
            {
                IsOnline = false;
                return (false, $"Error: {ex.Message}");
            }
        }


        // ─── Dispatch Schedule CRUD ──────────────────────────────────────────────

        public async Task<int> FetchMonthlyTargetAsync()
        {
            try
            {
                string json = await _http.GetStringAsync($"{BaseUrl}/MonthlyTarget.json");
                if (!string.IsNullOrWhiteSpace(json) && json != "null")
                    return int.Parse(json);
            }
            catch { }
            return 0;
        }

        public async Task<string> SaveMonthlyTargetAsync(int target)
        {
            try
            {
                var content = new StringContent(target.ToString(), System.Text.Encoding.UTF8, "application/json");
                var res = await _http.PutAsync($"{BaseUrl}/MonthlyTarget.json", content);
                return res.IsSuccessStatusCode ? "" : $"HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public async Task<(List<DispatchScheduleEntry> Entries, string Error)> FetchDispatchScheduleAsync()
        {
            try
            {
                string json = await _http.GetStringAsync($"{BaseUrl}/DispatchSchedule.json");
                return (ParseDispatchSchedule(json), "");
            }
            catch (Exception ex) { return (new List<DispatchScheduleEntry>(), ex.Message); }
        }

        public async Task<string> SaveDispatchScheduleAsync(DispatchScheduleEntry entry)
        {
            try
            {
                var payload = Newtonsoft.Json.JsonConvert.SerializeObject(new {
                    ProductName   = entry.ProductName,
                    ScheduledDate = entry.ScheduledDate,
                    Quantity      = entry.Quantity,
                    Remarks       = entry.Remarks
                });
                var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var res = await _http.PostAsync($"{BaseUrl}/DispatchSchedule.json", content);
                return res.IsSuccessStatusCode ? "" : $"HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public async Task<string> SaveDispatchScheduleListAsync(List<DispatchScheduleEntry> entries)
        {
            try
            {
                // We'll save it as an object where keys are random IDs, or just push.
                // To overwrite the whole node, we can serialize as a list or a dictionary.
                // Firebase treats arrays weirdly, so we should convert to dictionary with Guid keys
                var dict = new Dictionary<string, object>();
                foreach (var e in entries)
                {
                    dict[Guid.NewGuid().ToString("N")] = new {
                        ProductName   = e.ProductName,
                        ScheduledDate = e.ScheduledDate,
                        Quantity      = e.Quantity,
                        Remarks       = e.Remarks
                    };
                }
                var payload = Newtonsoft.Json.JsonConvert.SerializeObject(dict);
                var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var res = await _http.PutAsync($"{BaseUrl}/DispatchSchedule.json", content); // Put to overwrite
                return res.IsSuccessStatusCode ? "" : $"HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        public async Task<string> DeleteDispatchScheduleAsync(string firebaseKey)
        {
            try
            {
                var res = await _http.DeleteAsync($"{BaseUrl}/DispatchSchedule/{Uri.EscapeDataString(firebaseKey)}.json");
                return res.IsSuccessStatusCode ? "" : $"HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ─── Daily Plan CRUD ─────────────────────────────────────────────────────
        
        public async Task<Dictionary<string, DailyTargets>> FetchDailyPlanAsync()
        {
            try
            {
                string json = await _http.GetStringAsync($"{BaseUrl}/DailyPlan.json");
                return ParseDailyPlan(json);
            }
            catch { return new Dictionary<string, DailyTargets>(); }
        }

        public async Task<string> SaveDailyPlanAsync(Dictionary<string, DailyTargets> plans)
        {
            try
            {
                var payload = Newtonsoft.Json.JsonConvert.SerializeObject(plans);
                var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
                var res = await _http.PutAsync($"{BaseUrl}/DailyPlan.json", content);
                return res.IsSuccessStatusCode ? "" : $"HTTP {(int)res.StatusCode}";
            }
            catch (Exception ex) { return ex.Message; }
        }

        // ─── Parsers & Helpers ───────────────────────────────────────────────────

        private CombinedRecord ParseNodeToRecord(string macId, JObject node)
        {
            AssemblyRecord? assembly = null;
            TestingRecord?  testing  = null;
            PackingRecord?  packing  = null;
            RcaRecord?      rca      = null;
            CameraLinkRecord? camLink = null;

            var asmToken  = node["AssemblyApp"];
            var testToken = node["TestingApp"];
            var packToken = node["PackingApp"];
            var rcaToken  = node["RcaApp"];
            var camLinkToken = node["CameraLinkApp"];

            if (asmToken != null && asmToken.Type != JTokenType.Null)
            {
                assembly = asmToken.ToObject<AssemblyRecord>();
                if (assembly != null && asmToken["Parts"] is JObject parts)
                {
                    assembly.Parts = new Dictionary<string, string>();
                    foreach (var p in parts.Properties())
                        assembly.Parts[p.Name] = p.Value?.ToString() ?? "";
                }
            }

            if (packToken != null && packToken.Type != JTokenType.Null)
                packing = packToken.ToObject<PackingRecord>();

            if (testToken != null && testToken.Type != JTokenType.Null)
                testing = testToken.ToObject<TestingRecord>();

            if (rcaToken != null && rcaToken.Type != JTokenType.Null)
                rca = rcaToken.ToObject<RcaRecord>();

            if (camLinkToken != null && camLinkToken.Type != JTokenType.Null)
                camLink = camLinkToken.ToObject<CameraLinkRecord>();

            return new CombinedRecord
            {
                MacId      = macId,
                Assembly   = assembly,
                Testing    = testing,
                Packing    = packing,
                Rca        = rca,
                CameraLink = camLink
            };
        }

        private List<DispatchRecord> ParseDispatches(string json)
        {
            var list = new List<DispatchRecord>();
            if (string.IsNullOrWhiteSpace(json) || json == "null") return list;
            try
            {
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    var rec = prop.Value?.ToObject<DispatchRecord>();
                    if (rec != null) list.Add(rec);
                }
            }
            catch { /* Ignore parsing errors for secondary data */ }
            return list;
        }

        private Dictionary<string, SubAssyRecord> ParseSubAssy(string json)
        {
            var dict = new Dictionary<string, SubAssyRecord>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return dict;
            try
            {
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    var rec = prop.Value?.ToObject<SubAssyRecord>();
                    if (rec != null)
                    {
                        if (string.IsNullOrEmpty(rec.PcbaQR))
                            rec.PcbaQR = prop.Name;

                        dict[rec.PcbaQR.Trim()] = rec;
                    }
                }
            }
            catch { /* Ignore parsing errors for secondary data */ }
            return dict;
        }

        private async Task SafeTask(Task task)
        {
            try { await task; } catch { /* Ignore individual fetch errors */ }
        }

        private static string SanitizeKey(string key)
            => key.Replace("#", "_").Replace("$", "_").Replace("[", "_").Replace("]", "_").Replace("/", "_");

        /// <summary>Parses the full EndToEndTraceability node into a flat list of CombinedRecords (assembly + testing only).</summary>
        private List<CombinedRecord> ParseMainRecords(string json)
        {
            var list = new List<CombinedRecord>();
            if (string.IsNullOrWhiteSpace(json) || json == "null") return list;
            try
            {
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    if (prop.Value is not JObject node) continue;
                    
                    var asmToken = node["AssemblyApp"];
                    AssemblyRecord? assembly = null;
                    if (asmToken != null && asmToken.Type != JTokenType.Null)
                        assembly = asmToken.ToObject<AssemblyRecord>();

                    var testToken = node["TestingApp"];
                    TestingRecord? testing = null;
                    if (testToken != null && testToken.Type != JTokenType.Null)
                        testing = testToken.ToObject<TestingRecord>();
                        
                    var packToken = node["PackingApp"];
                    PackingRecord? packing = null;
                    if (packToken != null && packToken.Type != JTokenType.Null)
                        packing = packToken.ToObject<PackingRecord>();

                    list.Add(new CombinedRecord { MacId = prop.Name, Assembly = assembly, Testing = testing, Packing = packing });
                }
            }
            catch { }
            return list;
        }

        /// <summary>Parses the DispatchSchedule Firebase node into a list of entries.</summary>
        private List<DispatchScheduleEntry> ParseDispatchSchedule(string json)
        {
            var list = new List<DispatchScheduleEntry>();
            if (string.IsNullOrWhiteSpace(json) || json == "null") return list;
            try
            {
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    var entry = prop.Value?.ToObject<DispatchScheduleEntry>();
                    if (entry != null)
                    {
                        entry.FirebaseKey = prop.Name;
                        list.Add(entry);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>Parses the DailyPlan Firebase node into a dictionary.</summary>
        private Dictionary<string, DailyTargets> ParseDailyPlan(string json)
        {
            var dict = new Dictionary<string, DailyTargets>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(json) || json == "null") return dict;
            try
            {
                var root = JObject.Parse(json);
                foreach (var prop in root.Properties())
                {
                    if (prop.Value != null)
                    {
                        // Handle legacy data where the node is just an int
                        if (prop.Value.Type == JTokenType.Integer)
                        {
                            dict[prop.Name] = new DailyTargets { MainTarget = prop.Value.Value<int>() };
                        }
                        else
                        {
                            var dailyTarget = prop.Value.ToObject<DailyTargets>();
                            if (dailyTarget != null)
                                dict[prop.Name] = dailyTarget;
                        }
                    }
                }
            }
            catch { }
            return dict;
        }
    }
}
