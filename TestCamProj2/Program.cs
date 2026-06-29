using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NixMaster.Data;
using NixMaster.Core;

class Program {
    static async Task Main() {
        AppState.Settings = new NixMaster.Models.AppSettings {
            FirebaseUrl = "https://nix-traceability-default-rtdb.firebaseio.com/",
            NodePath = "EndToEndTraceability"
        };
        var reader = new FirebaseReader();
        var (records, err) = await reader.FetchAllAsync();
        Console.WriteLine($"Records: {records.Count}, Error: {err}");
        var r = records.FirstOrDefault(x => x.CamSubAssy != null);
        if (r != null) {
            Console.WriteLine($"CamSubAssy: {r.CamSubAssy.PcbaQR ?? "NULL"}");
        } else {
            Console.WriteLine("No CamSubAssy");
        }
    }
}
