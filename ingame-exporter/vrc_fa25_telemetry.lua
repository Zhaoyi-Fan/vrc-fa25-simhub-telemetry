-- VRC FA25 Telemetry exporter
-- Reads the car's private CAN/ECU channels (same source as the cockpit dash) and mirrors
-- them into a shared memory-mapped file for the SimHub plugin to read.
-- Read-only: does NOT modify any car file -> league checksums unaffected.
-- Contract: the fixed struct LAYOUT below is the channel reference; field order/types must
-- match the SimHub plugin (VrcData) exactly.

local MMF_NAME = 'AcTools.CSP.VRC_FA25.v1'

-- Fixed C-struct layout. Field ORDER + TYPES must match the SimHub plugin and the reader.
-- All fields are 4 bytes (int32_t / float) -> tight packing, no padding. bool -> int32 (0/1).
local LAYOUT = [[
  uint32_t counter;
  int32_t connected;

  int32_t strat;
  int32_t puMode;
  int32_t deploymentStrat;
  float kersInput;
  float kersDeployMJ;
  float kersRegenMJ;
  float mgukPowerKW;
  float mguhPowerKW;
  float frontMotorPowerKW;
  float puTemperature;
  int32_t overtake;
  int32_t kersAnti;

  float brakeBias;
  float brakeBiasLive;
  float brakeMigration;
  float brakeBiasTargetDelta;
  int32_t brakeShapeMap;
  float brakePressure;

  int32_t diffEntry;
  int32_t diffMid;
  int32_t diffExit;
  int32_t diffPower;
  int32_t diffCoast;

  int32_t fuelMap;
  int32_t pedalMap;
  int32_t torqueMap;
  int32_t engineBrake;
  float fuelUseTarget;
  float fuelUseLastLap;

  float torqueRequested;
  float torqueDriverRequested;
  float torqueOut;
  float throttleRaw;
  float throttlePedal;

  int32_t targetLapTimeMs;
  int32_t targetStintLaps;
  int32_t lapDeltaMode;

  int32_t isEngineRunning;
  int32_t isAntistallActive;
  int32_t isElectronicsBooted;
  int32_t isIgnitionStage1;
  int32_t isIgnitionStage2;
  int32_t isStarterCranking;
  int32_t isConstantSpeedLimiterActive;

  float plankWear;
  float frontLegalityWear;
  float midLegalityWear;
  float rearLegalityWear;

  int32_t aeroMap;
  int32_t aeroFrontWingGurney;
  int32_t aeroRearWingGurney;
  int32_t aeroLouvers;
  int32_t aeroFrontWingDamage;
  int32_t tyreCompoundRange;

  int32_t tyreTempDeltaFL;
  int32_t tyreTempDeltaFR;
  int32_t tyreTempDeltaRL;
  int32_t tyreTempDeltaRR;
  int32_t brakeDiscTempFL;
  int32_t brakeDiscTempFR;
  int32_t brakeDiscTempRL;
  int32_t brakeDiscTempRR;

  float gapAhead;
  float gapBehind;
  float perfDelta;

  int32_t isCaution;
]]

local mmf = ac.writeMemoryMappedFile(MMF_NAME, LAYOUT)

local inputs = nil
local counter = 0

-- For the AHEAD/BEHIND/delta replica: same custom leaderboard the cockpit dash builds
-- (display/styles/style_0/data.lua). Comparators defined once to avoid per-frame closures.
local sim = ac.getSim()
local leaderboard = {}
local function cmpRace(a, b) return (a.splinePosition + a.lapCount) > (b.splinePosition + b.lapCount) end
local function cmpQuali(a, b) return a.bestLapTimeMs > b.bestLapTimeMs end

-- Reconnect to the car's private CAN channel map (name -> {index, isBool}).
local function tryConnect()
  local id = ac.getCarID(0)
  if not id or not string.startsWith(id, 'vrc_formula_alpha_2025') then return false end
  local s = ac.load(id .. '_CAN')
  if type(s) ~= 'string' or s == '' then return false end
  local ok, parsed = pcall(stringify.parse, s)
  if not ok or type(parsed) ~= 'table' or not parsed[1] then return false end
  inputs = parsed[1].inputs
  return inputs ~= nil
end

-- Read a live channel by name (0 if unavailable).
local function rd(cphys, name)
  local m = inputs and inputs[name]
  if not m then return 0 end
  return cphys.scriptControllerInputs[m[1]] or 0
end

local function tick()
  local car = ac.getCar(0)
  local cphys = ac.getCarPhysics(0)
  if not car or not cphys then return end
  if not inputs then tryConnect() end

  counter = counter + 1
  mmf.counter = counter
  mmf.connected = inputs and 1 or 0

  -- ERS / hybrid
  mmf.strat = math.round((car.mgukDelivery or 0) + 1)          -- cockpit STRAT 1..8
  mmf.puMode = math.round(rd(cphys, 'puMode'))                 -- MODE 1..7 (no offset)
  mmf.deploymentStrat = math.round(rd(cphys, 'deploymentStrat') + 1)
  mmf.kersInput = rd(cphys, 'kersInput')
  mmf.kersDeployMJ = rd(cphys, 'kersDeployMJ')
  mmf.kersRegenMJ = rd(cphys, 'kersRegenMJ')
  mmf.mgukPowerKW = rd(cphys, 'rearMotorPowerKW')
  mmf.mguhPowerKW = rd(cphys, 'heatMotorPowerKW')
  mmf.frontMotorPowerKW = rd(cphys, 'frontMotorPowerKW')
  mmf.puTemperature = rd(cphys, 'puTemperature')
  mmf.overtake = math.round(rd(cphys, 'isHybridOvertakeActive'))
  mmf.kersAnti = math.round(rd(cphys, 'isHybridAntiActive'))

  -- brake (raw 0..1; SimHub multiplies x100 for %)
  mmf.brakeBias = rd(cphys, 'frontBias')                       -- = cockpit BB
  mmf.brakeBiasLive = rd(cphys, 'brakeBiasLive')              -- = frontBias + migration
  mmf.brakeMigration = rd(cphys, 'brakeMigration')
  mmf.brakeBiasTargetDelta = rd(cphys, 'brakeBiasTargetDelta')
  mmf.brakeShapeMap = math.round(rd(cphys, 'brakeShapeMap'))
  mmf.brakePressure = rd(cphys, 'brakePressure')

  -- differential
  mmf.diffEntry = math.round(rd(cphys, 'differentialEntrySetting') + 1)
  mmf.diffMid = math.round(rd(cphys, 'differentialMidSetting') + 1)
  mmf.diffExit = math.round(rd(cphys, 'differentialExitHispdSetting') + 1)
  mmf.diffPower = math.round(rd(cphys, 'differentialPower'))
  mmf.diffCoast = math.round(rd(cphys, 'differentialCoast'))

  -- engine / maps / fuel
  mmf.fuelMap = math.round(rd(cphys, 'fuelMap'))
  mmf.pedalMap = math.round(rd(cphys, 'pedalMap'))
  mmf.torqueMap = math.round(rd(cphys, 'torqueMap'))
  mmf.engineBrake = math.round(rd(cphys, 'engineBrakeSetting') + 1)
  mmf.fuelUseTarget = rd(cphys, 'fuelUseTarget')
  mmf.fuelUseLastLap = rd(cphys, 'fuelUseLastLap')

  -- torque / pedals (throttle raw 0..1; SimHub x100)
  mmf.torqueRequested = rd(cphys, 'torqueRequested')
  mmf.torqueDriverRequested = rd(cphys, 'torqueDriverRequested')
  mmf.torqueOut = rd(cphys, 'torqueOut')
  mmf.throttleRaw = rd(cphys, 'throttleRaw')
  mmf.throttlePedal = rd(cphys, 'throttlePedal')

  -- strategy targets
  local targetLapTimeMs = math.round(rd(cphys, 'driverTargetLapTime') * 10)
  local lapDeltaMode = math.round(rd(cphys, 'lapDelta'))
  mmf.targetLapTimeMs = targetLapTimeMs
  mmf.targetStintLaps = math.round(rd(cphys, 'driverTargetStintLaps'))
  mmf.lapDeltaMode = lapDeltaMode

  -- state (0/1)
  mmf.isEngineRunning = math.round(rd(cphys, 'isEngineRunning'))
  mmf.isAntistallActive = math.round(rd(cphys, 'isAntistallActive'))
  mmf.isElectronicsBooted = math.round(rd(cphys, 'isElectronicsBooted'))
  mmf.isIgnitionStage1 = math.round(rd(cphys, 'isIgnitionStage1'))
  mmf.isIgnitionStage2 = math.round(rd(cphys, 'isIgnitionStage2'))
  mmf.isStarterCranking = math.round(rd(cphys, 'isStarterCranking'))
  mmf.isConstantSpeedLimiterActive = math.round(rd(cphys, 'isConstantSpeedLimiterActive'))

  -- wear / legality (mm)
  mmf.plankWear = rd(cphys, 'plankWear')
  mmf.frontLegalityWear = rd(cphys, 'frontLegalityWear')
  mmf.midLegalityWear = rd(cphys, 'midLegalityWear')
  mmf.rearLegalityWear = rd(cphys, 'rearLegalityWear')

  -- aero
  mmf.aeroMap = math.round(rd(cphys, 'aeroMap'))
  mmf.aeroFrontWingGurney = math.round(rd(cphys, 'aeroFrontWingGurney'))
  mmf.aeroRearWingGurney = math.round(rd(cphys, 'aeroRearWingGurney'))
  mmf.aeroLouvers = math.round(rd(cphys, 'aeroLouvers'))
  mmf.aeroFrontWingDamage = math.round(rd(cphys, 'aeroFrontWingDamage'))
  mmf.tyreCompoundRange = math.round(rd(cphys, 'tyreCompoundRange'))

  -- tyres / brakes (match in-game display/instruments.lua exactly)
  local wheels = car.wheels
  -- Exact VRC formula (script_Tyres.lua): practical = 0.6*carcass + 0.4*surfaceAvg,
  -- PRACTICAL_TEMP_RATIO = 0.4. Carcass read from the same source the mod uses
  -- (ac.getCarPhysics); surface from StateCar I/M/O (accessCarPhysics isn't app-accessible).
  local function tyreDelta(i)
    if not wheels or not wheels[i] then return 0 end
    local w = wheels[i]
    local surfaceAvg = ((w.tyreInsideTemperature or 0) + (w.tyreMiddleTemperature or 0) + (w.tyreOutsideTemperature or 0)) / 3
    local pw = cphys.wheels[i]
    local carcass = (pw and pw.tyreCarcassTemperature) or 0
    -- match the mod exactly: practical is stored as uint8 (floored), dash then truncates delta
    local practical = math.floor(0.6 * carcass + 0.4 * surfaceAvg)
    local d = practical - (w.tyreOptimumTemperature or 0)
    d = d >= 0 and math.floor(d) or math.ceil(d) -- truncate toward zero, like %+03d
    return math.clamp(d, -99, 99)
  end
  local function discTemp(i)
    if not wheels or not wheels[i] then return 0 end
    return math.round(wheels[i].discTemperature or 0)
  end
  mmf.tyreTempDeltaFL = tyreDelta(0)
  mmf.tyreTempDeltaFR = tyreDelta(1)
  mmf.tyreTempDeltaRL = tyreDelta(2)
  mmf.tyreTempDeltaRR = tyreDelta(3)
  mmf.brakeDiscTempFL = discTemp(0)
  mmf.brakeDiscTempFR = discTemp(1)
  mmf.brakeDiscTempRL = discTemp(2)
  mmf.brakeDiscTempRR = discTemp(3)

  -- AHEAD / BEHIND / delta -- exact replica of display/styles/style_0/data.lua.
  -- Build the SAME custom leaderboard the cockpit uses (race: distance desc; else bestLap desc),
  -- pick the position-adjacent car, and use ac.getGapBetweenCars for the on-track time gap.
  local n = sim.carsCount
  for i = 0, n - 1 do leaderboard[i + 1] = ac.getCar(i) end
  for i = n + 1, #leaderboard do leaderboard[i] = nil end
  table.sort(leaderboard, sim.raceSessionType == 3 and cmpRace or cmpQuali)

  local pos
  for p = 1, n do
    if leaderboard[p].index == car.index then pos = p; break end
  end
  local aheadIdx = (pos and pos > 1) and leaderboard[pos - 1].index or nil
  local behindIdx = (pos and pos < n) and leaderboard[pos + 1].index or nil

  local gapAhead = 0
  if sim.isSessionStarted and car.racePosition > 1 and aheadIdx ~= nil then
    gapAhead = math.clamp(ac.getGapBetweenCars(car.index, aheadIdx), 0, 99.99)
  end
  local gapBehind = 0
  if sim.isSessionStarted and behindIdx ~= nil then
    gapBehind = math.clamp(math.abs(ac.getGapBetweenCars(car.index, behindIdx)), 0, 99.99)
  end
  mmf.gapAhead = gapAhead
  mmf.gapBehind = gapBehind

  -- Headline delta (in-game performanceMeterF): AC's predicted lap (best + performanceMeter)
  -- minus the mode-selected reference lap (0=previous, 1=best, 2=driver target).
  local estimatedMs = (car.bestLapTimeMs or 0) + (car.performanceMeter or 0) * 1000
  local comparisonMs = car.previousLapTimeMs or 0
  if lapDeltaMode == 2 then
    comparisonMs = targetLapTimeMs
  elseif lapDeltaMode == 1 then
    comparisonMs = car.bestLapTimeMs or 0
  end
  mmf.perfDelta = math.clamp((estimatedMs - comparisonMs) / 1000, -99.99, 99.99)

  -- race-flag caution: exact source the in-game wheel uses for the side yellow LED
  -- (lights.lua: sim.raceFlagType == ac.FlagType.Caution). Not in AC shared memory,
  -- so SimHub can't see it natively -> we publish it here.
  mmf.isCaution = (ac.FlagType and sim.raceFlagType == ac.FlagType.Caution) and 1 or 0
end

-- Run every frame whether or not the window is open: script.update covers the headless
-- case; windowMain covers builds that only tick while a window is shown. (Double-ticking a
-- frame is harmless - values are identical, counter is only a liveness signal.)
function script.update(dt)
  tick()
end

function script.windowMain(dt)
  tick()
  ui.text('MMF: ' .. MMF_NAME)
  ui.text(string.format('counter:   %d', counter))
  ui.text(string.format('connected: %s', tostring(mmf.connected == 1)))
  ui.separator()
  ui.text(string.format('STRAT %d   MODE %d', mmf.strat, mmf.puMode))
  ui.text(string.format('BB %.1f%%  live %.1f%%', mmf.brakeBias * 100, mmf.brakeBiasLive * 100))
  ui.text(string.format('Diff E/M/X  %d / %d / %d', mmf.diffEntry, mmf.diffMid, mmf.diffExit))
  ui.text(string.format('EB %d  FuelMap %d', mmf.engineBrake, mmf.fuelMap))
  ui.text(string.format('PU temp %.0f', mmf.puTemperature))
  ui.separator()
  ui.text('Leave this open (or minimized) while driving.')
end
