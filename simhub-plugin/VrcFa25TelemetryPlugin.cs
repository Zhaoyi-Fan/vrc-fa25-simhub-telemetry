using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using GameReaderCommon;
using SimHub.Plugins;

namespace VrcFa25Telemetry
{
    // Reads the shared memory-mapped file published by the in-game CSP exporter app
    // (apps/lua/vrc_fa25_telemetry) and exposes the VRC FA25 private channels as SimHub
    // properties. Standard data (speed/rpm/tyres/fuel/ERS%/gaps) comes from SimHub itself.
    // Struct layout MUST match vrc_fa25_telemetry.lua.
    [PluginDescription("VRC Formula Alpha 2025 private CAN/ECU telemetry via CSP shared memory.")]
    [PluginAuthor("VRC tooling")]
    [PluginName("VRC FA25 Telemetry")]
    public class VrcFa25TelemetryPlugin : IPlugin, IDataPlugin
    {
        private const string MmfName = "Local\\AcTools.CSP.VRC_FA25.v1";

        public PluginManager PluginManager { get; set; }

        private MemoryMappedFile _mmf;
        private MemoryMappedViewAccessor _acc;
        private VrcData _d;
        private int _retry;

        // --- popup state machine: setting splashes + lap popup (replicates display popups.lua) ---
        private const double SettingPopupSeconds = 1.5;   // in-game: displayPopupTime*0.1 (not exported)
        private const double LapPopupSeconds = 3.0;       // in-game: displayLapPopupTime*0.1
        private static readonly System.Globalization.CultureInfo Inv = System.Globalization.CultureInfo.InvariantCulture;
        private bool _popupInit;
        private int _splashKind;            // 0 = none, 1 = setting splash, 2 = lap popup
        private DateTime _splashUntil;
        private string _splashLabel = "", _splashValue = "";
        private string _splashColor = "#FF000000", _splashTextColor = "#FFFFFFFF";
        private double _splashValueFontSize = 280.0;
        private string _lastStrat = "", _lastPuMode = "", _lastEb = "", _lastDe = "", _lastDm = "",
                       _lastDx = "", _lastBbal = "", _lastBmig = "", _lastLaps = "", _lastTar = "";
        private int _lastCompletedLaps;
        private string _prevPerfDeltaText = "0.00";
        private string _lapNumber = "", _lapLastLap = "", _lapDelta = "", _lapFuelDelta = "";
        private string _lapFuelColor = "#FF801A1A", _lapFuelTextColor = "#FF000000";

        public void Init(PluginManager pluginManager)
        {
            TryOpen();

            // diagnostics
            this.AttachDelegate("Connected", () => _d.connected);
            this.AttachDelegate("Counter", () => _d.counter);

            // ERS / hybrid
            this.AttachDelegate("Strat", () => _d.strat);                 // 1..8 (cockpit)
            this.AttachDelegate("PuMode", () => _d.puMode);               // 1..7
            this.AttachDelegate("DeploymentStrat", () => _d.deploymentStrat);
            this.AttachDelegate("KersInput", () => _d.kersInput);
            this.AttachDelegate("KersDeployMJ", () => _d.kersDeployMJ);
            this.AttachDelegate("KersRegenMJ", () => _d.kersRegenMJ);
            this.AttachDelegate("MgukPowerKW", () => _d.mgukPowerKW);
            this.AttachDelegate("MguhPowerKW", () => _d.mguhPowerKW);
            this.AttachDelegate("FrontMotorPowerKW", () => _d.frontMotorPowerKW);
            this.AttachDelegate("PuTemperature", () => _d.puTemperature);
            this.AttachDelegate("Overtake", () => _d.overtake);
            this.AttachDelegate("KersAnti", () => _d.kersAnti);

            // brake (raw 0..1 + convenience % )
            this.AttachDelegate("BrakeBias", () => _d.brakeBias);
            this.AttachDelegate("BrakeBiasPct", () => _d.brakeBias * 100f);          // cockpit BB
            this.AttachDelegate("BrakeBiasLive", () => _d.brakeBiasLive);
            this.AttachDelegate("BrakeBiasLivePct", () => _d.brakeBiasLive * 100f);
            this.AttachDelegate("BrakeMigration", () => _d.brakeMigration);
            this.AttachDelegate("BrakeMigrationPct", () => _d.brakeMigration * 100f);
            this.AttachDelegate("BrakeBiasTargetDelta", () => _d.brakeBiasTargetDelta);
            this.AttachDelegate("BrakeShapeMap", () => _d.brakeShapeMap);
            this.AttachDelegate("BrakePressure", () => _d.brakePressure);

            // differential
            this.AttachDelegate("DiffEntry", () => _d.diffEntry);
            this.AttachDelegate("DiffMid", () => _d.diffMid);
            this.AttachDelegate("DiffExit", () => _d.diffExit);
            this.AttachDelegate("DiffPower", () => _d.diffPower);
            this.AttachDelegate("DiffCoast", () => _d.diffCoast);

            // engine / maps / fuel
            this.AttachDelegate("FuelMap", () => _d.fuelMap);
            this.AttachDelegate("PedalMap", () => _d.pedalMap);
            this.AttachDelegate("TorqueMap", () => _d.torqueMap);
            this.AttachDelegate("EngineBrake", () => _d.engineBrake);
            this.AttachDelegate("FuelUseTarget", () => Guard(_d.fuelUseTarget));   // isinf/NaN -> 0 (matches cockpit)
            this.AttachDelegate("FuelUseLastLap", () => Guard(_d.fuelUseLastLap));

            // torque / pedals
            this.AttachDelegate("TorqueRequested", () => _d.torqueRequested);
            this.AttachDelegate("TorqueDriverRequested", () => _d.torqueDriverRequested);
            this.AttachDelegate("TorqueOut", () => _d.torqueOut);
            this.AttachDelegate("ThrottleRawPct", () => _d.throttleRaw * 100f);
            this.AttachDelegate("ThrottlePedalPct", () => _d.throttlePedal * 100f);

            // strategy targets
            this.AttachDelegate("TargetLapTimeMs", () => _d.targetLapTimeMs);
            this.AttachDelegate("TargetLapTime", () =>
                _d.targetLapTimeMs > 0 ? TimeSpan.FromMilliseconds(_d.targetLapTimeMs).ToString(@"m\:ss\.fff") : "--");
            this.AttachDelegate("TargetStintLaps", () => _d.targetStintLaps);
            this.AttachDelegate("LapDeltaMode", () => _d.lapDeltaMode);

            // state
            this.AttachDelegate("IsEngineRunning", () => _d.isEngineRunning);
            this.AttachDelegate("IsAntistallActive", () => _d.isAntistallActive);
            this.AttachDelegate("IsElectronicsBooted", () => _d.isElectronicsBooted);
            this.AttachDelegate("IsIgnitionStage1", () => _d.isIgnitionStage1);
            this.AttachDelegate("IsIgnitionStage2", () => _d.isIgnitionStage2);
            this.AttachDelegate("IsStarterCranking", () => _d.isStarterCranking);
            this.AttachDelegate("IsConstantSpeedLimiterActive", () => _d.isConstantSpeedLimiterActive);

            // wear / legality (mm)
            this.AttachDelegate("PlankWear", () => _d.plankWear);
            this.AttachDelegate("FrontLegalityWear", () => _d.frontLegalityWear);
            this.AttachDelegate("MidLegalityWear", () => _d.midLegalityWear);
            this.AttachDelegate("RearLegalityWear", () => _d.rearLegalityWear);

            // aero
            this.AttachDelegate("AeroMap", () => _d.aeroMap);
            this.AttachDelegate("AeroFrontWingGurney", () => _d.aeroFrontWingGurney);
            this.AttachDelegate("AeroRearWingGurney", () => _d.aeroRearWingGurney);
            this.AttachDelegate("AeroLouvers", () => _d.aeroLouvers);
            this.AttachDelegate("AeroFrontWingDamage", () => _d.aeroFrontWingDamage);
            this.AttachDelegate("TyreCompoundRange", () => _d.tyreCompoundRange);

            // tyres (signed delta to optimum, matches in-game) + brake disc temps
            this.AttachDelegate("TyreTempDeltaFL", () => _d.tyreTempDeltaFL);
            this.AttachDelegate("TyreTempDeltaFR", () => _d.tyreTempDeltaFR);
            this.AttachDelegate("TyreTempDeltaRL", () => _d.tyreTempDeltaRL);
            this.AttachDelegate("TyreTempDeltaRR", () => _d.tyreTempDeltaRR);
            this.AttachDelegate("BrakeDiscTempFL", () => _d.brakeDiscTempFL);
            this.AttachDelegate("BrakeDiscTempFR", () => _d.brakeDiscTempFR);
            this.AttachDelegate("BrakeDiscTempRL", () => _d.brakeDiscTempRL);
            this.AttachDelegate("BrakeDiscTempRR", () => _d.brakeDiscTempRR);

            // exact in-game colours (optimumValueLerp). Tyres keyed off delta (optimum=0,
            // window +/-10, blue<=-20, red full at +30). Brakes off disc temp (200/600/1000, window 200).
            this.AttachDelegate("TyreColorFL", () => LerpColorHex(_d.tyreTempDeltaFL, 10, -20, 0, 20));
            this.AttachDelegate("TyreColorFR", () => LerpColorHex(_d.tyreTempDeltaFR, 10, -20, 0, 20));
            this.AttachDelegate("TyreColorRL", () => LerpColorHex(_d.tyreTempDeltaRL, 10, -20, 0, 20));
            this.AttachDelegate("TyreColorRR", () => LerpColorHex(_d.tyreTempDeltaRR, 10, -20, 0, 20));
            this.AttachDelegate("BrakeColorFL", () => LerpColorHex(_d.brakeDiscTempFL, 200, 200, 600, 1000));
            this.AttachDelegate("BrakeColorFR", () => LerpColorHex(_d.brakeDiscTempFR, 200, 200, 600, 1000));
            this.AttachDelegate("BrakeColorRL", () => LerpColorHex(_d.brakeDiscTempRL, 200, 200, 600, 1000));
            this.AttachDelegate("BrakeColorRR", () => LerpColorHex(_d.brakeDiscTempRR, 200, 200, 600, 1000));

            // AHEAD / BEHIND / headline delta -- exact in-game replica. Computed in the exporter
            // from the mod's own leaderboard + ac.getGapBetweenCars + AC performanceMeter, so these
            // match the cockpit instead of SimHub's on-track-relative gaps / LiveDeltaToBest.
            this.AttachDelegate("GapAhead", () => _d.gapAhead);     // seconds, 0 when no car ahead
            this.AttachDelegate("GapBehind", () => _d.gapBehind);   // seconds, 0 when no car behind
            this.AttachDelegate("PerfDelta", () => _d.perfDelta);   // seconds, signed
            this.AttachDelegate("PerfDeltaText", () => FormatPerfDelta(_d.perfDelta));

            // race-flag caution (in-game wheel side yellow LED). Not in AC shared memory; from exporter.
            this.AttachDelegate("IsCaution", () => _d.isCaution);

            // setting splash + lap popup (state machine driven in UpdatePopups)
            this.AttachDelegate("SplashKind", () => _splashKind);          // 0 none / 1 setting / 2 lap
            this.AttachDelegate("SplashLabel", () => _splashLabel);
            this.AttachDelegate("SplashValue", () => _splashValue);
            this.AttachDelegate("SplashColor", () => _splashColor);
            this.AttachDelegate("SplashTextColor", () => _splashTextColor);
            this.AttachDelegate("SplashValueFontSize", () => _splashValueFontSize);
            this.AttachDelegate("LapNumber", () => _lapNumber);
            this.AttachDelegate("LapLastLap", () => _lapLastLap);
            this.AttachDelegate("LapDelta", () => _lapDelta);
            this.AttachDelegate("LapFuelDelta", () => _lapFuelDelta);
            this.AttachDelegate("LapFuelColor", () => _lapFuelColor);
            this.AttachDelegate("LapFuelTextColor", () => _lapFuelTextColor);
        }

        public void DataUpdate(PluginManager pluginManager, ref GameData data)
        {
            if (_acc == null)
            {
                // retry opening roughly once a second until the in-game exporter creates the MMF
                if (++_retry >= 60) { _retry = 0; TryOpen(); }
                return;
            }
            try { _acc.Read(0, out _d); UpdatePopups(data); }
            catch { Close(); }
        }

        public void End(PluginManager pluginManager) { Close(); }

        private void TryOpen()
        {
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(MmfName, MemoryMappedFileRights.Read);
                _acc = _mmf.CreateViewAccessor(0, Marshal.SizeOf(typeof(VrcData)), MemoryMappedFileAccess.Read);
            }
            catch
            {
                Close();
            }
        }

        private void Close()
        {
            _acc?.Dispose();
            _mmf?.Dispose();
            _acc = null;
            _mmf = null;
        }

        // Replicates display/instruments.lua optimumValueLerp() exactly: flat optimum colour
        // within +/-range of optimum, lerp toward low/high colours outside (deltaLow/deltaHigh),
        // solid low colour below lowValue. Pure blue/green/red = rgbm.colors.* used in-game.
        private static readonly int[] CBlue = { 0, 0, 255 };
        private static readonly int[] CGreen = { 0, 128, 0 }; // rgbm.colors.green = rgbm(0,0.5,0) = #008000 (NOT pure green)
        private static readonly int[] CRed = { 255, 0, 0 };

        private static int[] Lerp(int[] a, int[] b, double t)
        {
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return new[]
            {
                (int)Math.Round(a[0] + (b[0] - a[0]) * t),
                (int)Math.Round(a[1] + (b[1] - a[1]) * t),
                (int)Math.Round(a[2] + (b[2] - a[2]) * t)
            };
        }

        // Matches display/styles/style_0/data.lua performanceMeter formatting exactly:
        // "0.00" when the delta is zero (no data), otherwise signed with 2 decimals, switching to
        // 1 decimal once |delta| >= 10. Invariant culture so the decimal separator stays '.'.
        private static string FormatPerfDelta(float v)
        {
            if (v == 0f) return "0.00";
            double a = Math.Abs(v);
            string sign = v > 0f ? "+" : "-";
            return sign + a.ToString(a >= 10.0 ? "0.0" : "0.00", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string LerpColorHex(double input, double range, double lowValue, double optimumValue, double highValue)
        {
            double inputFloor = Math.Floor(input);
            double deltaHigh = highValue - optimumValue + range;
            double deltaLow = optimumValue - lowValue - range;
            int[] c;
            if (input >= optimumValue - range && input < optimumValue + range) c = CGreen;
            else if (input >= lowValue && input < optimumValue - range) c = Lerp(CGreen, CBlue, (optimumValue - inputFloor - range) / deltaLow);
            else if (input >= optimumValue + range) c = Lerp(CGreen, CRed, (inputFloor - optimumValue) / deltaHigh);
            else c = CBlue;
            return string.Format("#FF{0:X2}{1:X2}{2:X2}", c[0], c[1], c[2]);
        }

        // Replicates display/styles/style_0/popups.lua: a value-change on any setting shows a
        // full-screen splash for ~1.5s; a completed lap shows the lap popup for ~3s; new triggers
        // override the current one (lap takes priority on a tie, matching the in-game splasher list).
        private void UpdatePopups(GameData data)
        {
            var now = DateTime.UtcNow;
            if (_d.connected != 1) { _popupInit = false; _splashKind = 0; return; }

            int laps = (data != null && data.NewData != null) ? data.NewData.CompletedLaps : _lastCompletedLaps;

            if (!_popupInit)
            {
                SnapshotSettings();
                _lastCompletedLaps = laps;
                _prevPerfDeltaText = FormatPerfDelta(_d.perfDelta);
                _popupInit = true;
                _splashKind = 0;
                return;
            }

            if (_splashKind != 0 && now >= _splashUntil) _splashKind = 0;

            if (laps > _lastCompletedLaps)
            {
                _lapNumber = "LAP " + (laps + 1).ToString(Inv);
                _lapLastLap = (data != null && data.NewData != null) ? FormatLap(data.NewData.LastLapTime) : "--:--.---";
                _lapDelta = _prevPerfDeltaText; // perf meter from just before the line
                float fd = Guard(_d.fuelUseTarget) - Guard(_d.fuelUseLastLap);
                bool pos = fd > 0;
                _lapFuelDelta = (fd > 0 ? "+" : (fd < 0 ? "-" : "")) + Math.Abs(fd).ToString("0.00", Inv);
                _lapFuelColor = pos ? "#FF008033" : "#FF801A1A";        // rbrGreen / darkRed
                _lapFuelTextColor = pos ? "#FFFFFFFF" : "#FF000000";
                _splashKind = 2;
                _splashUntil = now.AddSeconds(LapPopupSeconds);
                _lastCompletedLaps = laps;
                SnapshotSettings(); // swallow any setting change on the same frame (lap wins)
            }
            else
            {
                if (laps != _lastCompletedLaps) _lastCompletedLaps = laps; // session reset/decrement: resync, no popup
                bool set = false;
                set = TrySetting(set, _d.strat.ToString(Inv),            ref _lastStrat, "STRAT",   "#FF008033", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.puMode.ToString(Inv),           ref _lastPuMode, "MODE",   "#FF8000FF", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.engineBrake.ToString(Inv),      ref _lastEb,    "EB",       "#FFFF4000", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.diffEntry.ToString(Inv),        ref _lastDe,    "ENTRY",    "#FF000000", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.diffMid.ToString(Inv),          ref _lastDm,    "MID",      "#FF000000", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.diffExit.ToString(Inv),         ref _lastDx,    "EXIT",     "#FF000000", "#FFFFFFFF", false, now);
                set = TrySetting(set, BbalString(),                      ref _lastBbal,  "BBal",     "#FFFFFFFF", "#FF000000", false, now);
                set = TrySetting(set, _d.brakeShapeMap.ToString(Inv),    ref _lastBmig,  "BMIG",     "#FF004DFF", "#FFFFFFFF", false, now);
                set = TrySetting(set, _d.targetStintLaps.ToString(Inv),  ref _lastLaps,  "LAPS",     "#FF333333", "#FFFFFFFF", false, now);
                set = TrySetting(set, TarString(),                       ref _lastTar,   "TAR LAP",  "#FF333333", "#FFFFFFFF", true,  now);
            }

            _prevPerfDeltaText = FormatPerfDelta(_d.perfDelta);
        }

        private bool TrySetting(bool already, string val, ref string last, string label, string color, string textColor, bool isTime, DateTime now)
        {
            bool changed = val != last;
            last = val;
            if (changed && !already)
            {
                _splashLabel = label;
                _splashValue = val;
                _splashColor = color;
                _splashTextColor = textColor;
                _splashValueFontSize = isTime ? 150.0 : 280.0; // laptime string is wider -> smaller
                _splashKind = 1;
                _splashUntil = now.AddSeconds(SettingPopupSeconds);
                return true;
            }
            return already;
        }

        private void SnapshotSettings()
        {
            _lastStrat = _d.strat.ToString(Inv);
            _lastPuMode = _d.puMode.ToString(Inv);
            _lastEb = _d.engineBrake.ToString(Inv);
            _lastDe = _d.diffEntry.ToString(Inv);
            _lastDm = _d.diffMid.ToString(Inv);
            _lastDx = _d.diffExit.ToString(Inv);
            _lastBbal = BbalString();
            _lastBmig = _d.brakeShapeMap.ToString(Inv);
            _lastLaps = _d.targetStintLaps.ToString(Inv);
            _lastTar = TarString();
        }

        private string BbalString() { return (_d.brakeBias * 100f).ToString("0.0", Inv); }
        private string TarString() { return FormatLap(TimeSpan.FromMilliseconds(_d.targetLapTimeMs)); }
        private static string FormatLap(TimeSpan ts) { return ts.TotalMilliseconds > 0 ? ts.ToString(@"m\:ss\.fff") : "--:--.---"; }
        private static float Guard(float v) { return (float.IsNaN(v) || float.IsInfinity(v)) ? 0f : v; }
    }

    // Field order/types MUST match the LAYOUT string in vrc_fa25_telemetry.lua.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct VrcData
    {
        public uint counter;
        public int connected;

        public int strat;
        public int puMode;
        public int deploymentStrat;
        public float kersInput;
        public float kersDeployMJ;
        public float kersRegenMJ;
        public float mgukPowerKW;
        public float mguhPowerKW;
        public float frontMotorPowerKW;
        public float puTemperature;
        public int overtake;
        public int kersAnti;

        public float brakeBias;
        public float brakeBiasLive;
        public float brakeMigration;
        public float brakeBiasTargetDelta;
        public int brakeShapeMap;
        public float brakePressure;

        public int diffEntry;
        public int diffMid;
        public int diffExit;
        public int diffPower;
        public int diffCoast;

        public int fuelMap;
        public int pedalMap;
        public int torqueMap;
        public int engineBrake;
        public float fuelUseTarget;
        public float fuelUseLastLap;

        public float torqueRequested;
        public float torqueDriverRequested;
        public float torqueOut;
        public float throttleRaw;
        public float throttlePedal;

        public int targetLapTimeMs;
        public int targetStintLaps;
        public int lapDeltaMode;

        public int isEngineRunning;
        public int isAntistallActive;
        public int isElectronicsBooted;
        public int isIgnitionStage1;
        public int isIgnitionStage2;
        public int isStarterCranking;
        public int isConstantSpeedLimiterActive;

        public float plankWear;
        public float frontLegalityWear;
        public float midLegalityWear;
        public float rearLegalityWear;

        public int aeroMap;
        public int aeroFrontWingGurney;
        public int aeroRearWingGurney;
        public int aeroLouvers;
        public int aeroFrontWingDamage;
        public int tyreCompoundRange;

        public int tyreTempDeltaFL;
        public int tyreTempDeltaFR;
        public int tyreTempDeltaRL;
        public int tyreTempDeltaRR;
        public int brakeDiscTempFL;
        public int brakeDiscTempFR;
        public int brakeDiscTempRL;
        public int brakeDiscTempRR;

        public float gapAhead;
        public float gapBehind;
        public float perfDelta;
        public int isCaution;
    }
}
