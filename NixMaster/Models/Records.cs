using System.Collections.Generic;
using System.Linq;

namespace NixMaster.Models
{
    /// <summary>
    /// Assembly data saved by NixTraceability under:
    ///   EndToEndTraceability/{MAC_ID}/AssemblyApp.json
    /// </summary>
    public class AssemblyRecord
    {
        public long   RecordId    { get; set; }
        public string Operator    { get; set; } = "";
        public string Shift       { get; set; } = "";
        public string Batch       { get; set; } = "";
        public string Timestamp   { get; set; } = "";
        public string StationName { get; set; } = "";
        public string Status      { get; set; } = "OK";
        public Dictionary<string, string> Parts { get; set; } = new();
    }

    /// <summary>
    /// Packing data saved by NixPackTrace under:
    ///   EndToEndTraceability/{MAC_ID}/PackingApp.json
    /// </summary>
    public class PackingRecord
    {
        public string MacId       { get; set; } = "";
        public string BoxNo       { get; set; } = "";
        public string PackedAt    { get; set; } = "";
        public string PackedBy    { get; set; } = "";
        public string LongQR      { get; set; } = "";
        public string ShortQR     { get; set; } = "";
        public string Status      { get; set; } = "";
        public string StationName { get; set; } = "";

        // Dispatch fields
        public string DispatchDate { get; set; } = "";
        public string DispatchedBy { get; set; } = "";
        public string DispatchRemarks { get; set; } = "";
    }

    /// <summary>
    /// Testing data saved by NixTestTrace under:
    ///   EndToEndTraceability/{MAC_ID}/TestingApp.json
    /// </summary>
    public class TestingRecord
    {
        public int Id { get; set; }
        public string MacId { get; set; } = "";
        public string DeviceSerialNo { get; set; } = "";
        public string TestingQR { get; set; } = "";
        public string StationName { get; set; } = "Testing";
        public string Operator { get; set; } = "Unknown";
        public string Status { get; set; } = "OK";
        public string DefectDetails { get; set; } = "";
        public string TestedAt { get; set; } = "";
    }

    /// <summary>
    /// RCA data saved by NixRcaTrace under:
    ///   EndToEndTraceability/{MAC_ID}/RcaApp.json
    /// </summary>
    public class RcaRecord
    {
        public string RcaDate { get; set; } = "";
        public string RootCause { get; set; } = "";
        public string Engineer { get; set; } = "";
        public string ActionTaken { get; set; } = "";
        public string HandoverTo { get; set; } = "";
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// Camera data saved by NixCameraLink under:
    ///   EndToEndTraceability/{MAC_ID}/CameraLinkApp.json
    /// </summary>
    public class CameraLinkRecord
    {
        public string PcbaId { get; set; } = "";
        public string SubAssyId { get; set; } = "";
        public string ScannedAt { get; set; } = "";
        public string ScannedBy { get; set; } = "";
        public string StationName { get; set; } = "Camera Link";
        public string Status { get; set; } = "OK";
    }

    /// <summary>
    /// SubAssembly data saved by Nixirtrace under mode nodes.
    /// </summary>
    public class SubAssyRecord
    {
        public string PcbaQR { get; set; } = "";
        public string ScannedBy { get; set; } = "";
        public string ScannedAt { get; set; } = "";
    }

    /// <summary>
    /// Dispatch data saved by NixPackTrace under Dispatches node.
    /// </summary>
    public class DispatchRecord
    {
        public string DispatchId { get; set; } = "";
        public string FromBoxNo { get; set; } = "";
        public string ToBoxNo { get; set; } = "";
        public string DispatchDate { get; set; } = "";
        
        [Newtonsoft.Json.JsonProperty("Operator")]
        public string DispatchedBy { get; set; } = "";
        public string Remarks { get; set; } = "";
        
        public int BoxCount { get; set; }
        public int TotalUnits { get; set; }
        public string PalletId { get; set; } = "";
        public string Source { get; set; } = "";
        public System.Collections.Generic.List<string> BoxNumbers { get; set; } = new System.Collections.Generic.List<string>();
    }

    /// <summary>
    /// Combined record — one row per MAC ID.
    /// Assembly is always present; Packing may be null (not yet packed).
    /// </summary>
    public class CombinedRecord
    {
        public string          MacId    { get; set; } = "";
        public AssemblyRecord? Assembly { get; set; }
        public TestingRecord?  Testing  { get; set; }
        public PackingRecord?  Packing  { get; set; }
        public DispatchRecord? Dispatch { get; set; }
        public RcaRecord?      Rca      { get; set; }
        public CameraLinkRecord? CameraLink { get; set; }
        public SubAssyRecord?  IrSubAssy { get; set; }
        public SubAssyRecord?  IrIndicationSubAssy { get; set; }
        public SubAssyRecord?  CamSubAssy { get; set; }

        public bool IsPacked => Packing != null;
        public bool IsTested => Testing != null;
        public bool IsTestingNG => Testing?.Status.Equals("NG", System.StringComparison.OrdinalIgnoreCase) == true;
        public bool IsRework => Assembly?.Parts?.Values.Any(v => v != null && v.IndexOf("rework", System.StringComparison.OrdinalIgnoreCase) >= 0) ?? false;
        public bool IsRcaCompleted => Rca != null;
        public bool IsDispatched => Dispatch != null;
    }

    /// <summary>
    /// A scheduled dispatch entry managed from the Dispatch Schedule screen.
    /// Saved under Firebase: DispatchSchedule/{key}.json
    /// </summary>
    public class DispatchScheduleEntry
    {
        public string FirebaseKey   { get; set; } = "";   // populated after fetch
        public string ProductName   { get; set; } = "";   // e.g. "IR PCBA SUB ASSy"
        public string ScheduledDate { get; set; } = "";   // ISO date "yyyy-MM-dd"
        public int    Quantity      { get; set; }         // Number of items to dispatch
        public string Remarks       { get; set; } = "";
    }

    /// <summary>
    /// Daily production plan and targets for all assemblies
    /// </summary>
    public class DailyTargets
    {
        public int MainTarget { get; set; }
        public int TestingTarget { get; set; }
        public Dictionary<string, int> SubAssyTargets { get; set; } = new(System.StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Pre-calculated stats for a single product, used by the Line Status Dashboard.
    /// </summary>
    public class ProductLineStats
    {
        public string ProductName    { get; set; } = "";
        public int TodayAssembled    { get; set; }
        public int TodayTarget       { get; set; }
        public int CurrentMonthAssembled { get; set; }
        public int MonthlyTarget     { get; set; }
        public int TodayTested       { get; set; }
        public int TodayTestingTarget { get; set; }
        public int TodayDefects      { get; set; }
        public int Inventory         { get; set; }
        public bool   IsRunning      { get; set; } // true = scan within last 15 min
    }
}
