using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using NAudio.CoreAudioApi;

namespace OmniHub.Core;

public record DetectedDevice(string Category, string Name, string Vendor, string Detail);

/// <summary>Escaneo universal del hardware de cualquier PC (WMI + PnP + audio).</summary>
public class HardwareReport
{
    public string Motherboard = "Desconocida";
    public string MotherboardVendor = "";
    public string Cpu = "Desconocida";
    public List<string> Gpus = new();
    public List<string> Ram = new();
    public List<DetectedDevice> Peripherals = new();
    public List<string> AudioOutputs = new();
    public List<string> VendorAppsRunning = new();

    public bool HasRgbCandidates => Peripherals.Any(p => p.Category is "RGB" or "Ratón" or "Teclado" or "Auriculares")
        || Regex.IsMatch(MotherboardVendor, "MSI|Micro-Star|ASUS|ASUSTeK|Gigabyte|ASRock|NZXT|Biostar", RegexOptions.IgnoreCase)
        || Ram.Any(r => Regex.IsMatch(r, "G.Skill|Corsair|Kingston|TeamGroup|Crucial|ADATA|XPG", RegexOptions.IgnoreCase));

    public bool HasHeadset => AudioOutputs.Any(a => Regex.IsMatch(a, "head|auricular|USB|INZONE|Arctis|HyperX|Cloud|Kraken|BlackShark|Astro|Virtuoso|HS\\d|G\\d{3}|Nari|BlackShark", RegexOptions.IgnoreCase))
        || Peripherals.Any(p => p.Category == "Auriculares");
}

public static class HardwareScan
{
    // VID USB -> (fabricante, categoría probable)
    static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["046D"] = "Logitech", ["1532"] = "Razer", ["1B1C"] = "Corsair", ["1038"] = "SteelSeries",
        ["0951"] = "HyperX", ["03F0"] = "HyperX/HP", ["054C"] = "Sony", ["1E7D"] = "Roccat/Turtle Beach",
        ["258A"] = "Glorious/SinoWealth", ["1E71"] = "NZXT", ["0DB0"] = "MSI", ["1462"] = "MSI",
        ["0B05"] = "ASUS", ["1044"] = "Gigabyte", ["048D"] = "Gigabyte/ITE (RGB)", ["26CE"] = "ASRock (RGB)",
        ["3633"] = "Glorious", ["320F"] = "Glorious/Evision", ["0C45"] = "Redragon/Microdia", ["04D9"] = "Holtek (teclados)",
        ["1A2C"] = "Teclado genérico", ["3434"] = "Keychron", ["2516"] = "Cooler Master", ["24AE"] = "Rapoo",
        ["2EA8"] = "Endgame Gear", ["361D"] = "Lamzu", ["373B"] = "Pulsar/Compx", ["093A"] = "Pixart (ratones)",
        ["1BCF"] = "Sunplus", ["0738"] = "Mad Catz", ["2F68"] = "Hoksi/Ajazz", ["0B0E"] = "Jabra", ["1395"] = "EPOS/Sennheiser",
        ["12D1"] = "Huawei", ["045E"] = "Microsoft", ["2F45"] = "Lian Li", ["0CF2"] = "Lian Li (ENE)", ["1A86"] = "QinHeng (RGB)",
        ["16D0"] = "Hubs RGB", ["2109"] = "VIA", ["0416"] = "Winbond/Thermaltake", ["264A"] = "Thermaltake", ["1D50"] = "OpenMoko/DIY",
    };

    public static HardwareReport Run()
    {
        var r = new HardwareReport();
        Try(() =>
        {
            foreach (var o in Query("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            {
                r.MotherboardVendor = $"{o["Manufacturer"]}".Trim();
                r.Motherboard = $"{o["Manufacturer"]} {o["Product"]}".Trim();
            }
        });
        Try(() => { foreach (var o in Query("SELECT Name FROM Win32_Processor")) r.Cpu = $"{o["Name"]}".Trim(); });
        Try(() =>
        {
            foreach (var o in Query("SELECT Name FROM Win32_VideoController"))
            {
                var n = $"{o["Name"]}".Trim();
                if (!Regex.IsMatch(n, "Virtual|Parsec|Basic Display|Remote", RegexOptions.IgnoreCase)) r.Gpus.Add(n);
            }
        });
        Try(() =>
        {
            foreach (var o in Query("SELECT Manufacturer, PartNumber, Capacity FROM Win32_PhysicalMemory"))
            {
                var gb = Convert.ToDouble(o["Capacity"] ?? 0) / 1073741824.0;
                var part = $"{o["PartNumber"]}".Trim();
                var man = $"{o["Manufacturer"]}".Trim();
                // Muchos módulos reportan "Unknown": deducimos la marca por el código de pieza
                if (man is "" or "Unknown" or "Undefined")
                    man = part switch
                    {
                        _ when Regex.IsMatch(part, "^F[3-5]-") => "G.Skill",
                        _ when part.StartsWith("CM") => "Corsair",
                        _ when part.StartsWith("KF") || part.StartsWith("KHX") => "Kingston Fury",
                        _ when part.StartsWith("TF") || part.StartsWith("TLZ") || part.StartsWith("FF") => "TeamGroup",
                        _ when part.StartsWith("CT") || part.StartsWith("BL") => "Crucial",
                        _ when part.StartsWith("AX") => "ADATA XPG",
                        _ => man
                    };
                r.Ram.Add($"{man} {part}".Trim() + $" ({gb:0} GB)");
            }
        });

        // Periféricos USB/HID/Bluetooth
        Try(() =>
        {
            var seen = new HashSet<string>();
            foreach (var o in Query("SELECT Name, DeviceID, PNPClass FROM Win32_PnPEntity WHERE DeviceID LIKE 'USB\\\\VID_%' OR DeviceID LIKE 'HID\\\\VID_%'"))
            {
                var id = $"{o["DeviceID"]}";
                var m = Regex.Match(id, @"VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var vid = m.Groups[1].Value.ToUpperInvariant();
                var pid = m.Groups[2].Value.ToUpperInvariant();
                if (!Vendors.TryGetValue(vid, out var vendor)) continue;
                var name = $"{o["Name"]}";
                var cls = $"{o["PNPClass"]}";
                var key = vid + pid;
                // Preferimos el nombre "bonito" (USB) frente a los genéricos HID
                bool generic = Regex.IsMatch(name, "compatible con HID|HID-compliant|teclado HID|HID Keyboard|Dispositivo de entrada USB|USB Input Device|Dispositivo compuesto|Composite|Dispositivo de juego|game controller", RegexOptions.IgnoreCase);
                var cat = Regex.IsMatch(name, "Controller|Gamepad|DualSense|DualShock|Mando", RegexOptions.IgnoreCase) ? "Mando" : cls switch
                {
                    "Mouse" => "Ratón",
                    "Keyboard" => "Teclado",
                    "MEDIA" or "AudioEndpoint" => "Auriculares",
                    _ => GuessCategory(name, vendor)
                };
                var existing = r.Peripherals.FindIndex(p => p.Detail == $"{vid}:{pid}");
                if (existing >= 0)
                {
                    var e = r.Peripherals[existing];
                    var betterName = generic || !IsGenericName(e.Name, vendor) ? e.Name : name;
                    var betterCat = Rank(cat) < Rank(e.Category) ? cat : e.Category;
                    r.Peripherals[existing] = e with { Name = betterName, Category = betterCat };
                    continue;
                }
                if (!seen.Add(key)) continue;
                r.Peripherals.Add(new DetectedDevice(cat, generic ? $"{vendor} {pid}" : name, vendor, $"{vid}:{pid}"));
            }
        });

        Try(() =>
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                r.AudioOutputs.Add(d.FriendlyName);
        });

        Try(() => r.VendorAppsRunning = VendorApps.Running().Select(v => v.Name).ToList());
        return r;
    }

    // Un dispositivo expone varias interfaces (un ratón suele tener también una de "teclado"): gana la más específica
    static int Rank(string cat) => cat switch { "Mando" => 0, "Ratón" => 1, "Auriculares" => 2, "Teclado" => 3, "RGB" => 4, _ => 5 };
    static bool IsGenericName(string n, string vendor) => n.StartsWith(vendor + " ") && n.Length <= vendor.Length + 5;

    static string GuessCategory(string name, string vendor)
    {
        if (Regex.IsMatch(name, "mouse|ratón|G\\d{3}|Viper|DeathAdder|Basilisk|Rival|Aerox|Pulsefire|Model [ODI]", RegexOptions.IgnoreCase)) return "Ratón";
        if (Regex.IsMatch(name, "keyboard|teclado|Huntsman|BlackWidow|Apex|Alloy|K\\d{2}", RegexOptions.IgnoreCase)) return "Teclado";
        if (Regex.IsMatch(name, "head|auricular|INZONE|Arctis|Cloud|Kraken|HS\\d|Virtuoso|Astro|audio", RegexOptions.IgnoreCase)) return "Auriculares";
        if (Regex.IsMatch(vendor, "RGB|NZXT|Lian Li|Thermaltake|ASRock|ITE|MSI|ASUS", RegexOptions.IgnoreCase)) return "RGB";
        return "Dispositivo";
    }

    static IEnumerable<ManagementBaseObject> Query(string q)
    {
        using var s = new ManagementObjectSearcher(q);
        foreach (var o in s.Get()) yield return o;
    }

    static void Try(Action a) { try { a(); } catch (Exception e) { Debug.WriteLine(e); } }
}
