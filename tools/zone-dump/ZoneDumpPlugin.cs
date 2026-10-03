using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;

namespace QuestCodex.ZoneDump;

// Dev tool (not shipped): captures quest zone coordinates once per map without accepting any quest.
// Dumps once per raid automatically 15 s after the raid starts (0.0.5, configurable), or any time with the dump key (F10).
// Positions are raw Unity world coordinates (x, y = height, z), the same space as the server's looseLoot.json,
// so no conversion happens here.
// 0.0.4: also exits (every ExfiltrationPoint subclass, incl. scav/shared/secret) and transit points, for maps whose
// tarkov.dev positions are missing or wrong (10 spec §7).
// 0.0.6: Icebreaker doors without a key (keypads, the explosive chain door) and named scene objects (11 spec §2).
// 0.0.7: quest switches — the Icebreaker breaker panels its client mod ties to VisitPlace targets (fix_element_*).
[BepInPlugin("com.viper.questcodex.zonedump", "QuestCodex Zone Dump", "0.0.7")]
public class ZoneDumpPlugin : BaseUnityPlugin
{
    private ConfigEntry<KeyboardShortcut> _dumpKey = null!;
    private ConfigEntry<bool> _autoDump = null!;
    private ConfigEntry<float> _autoDumpDelay = null!;
    private string? _autoDumpedLocation;
    private float _raidSeenAt = -1f;

    // ManimalIcebreaker doors that open without a key: keypads (doorId in the mod's icebreaker_passcodes.json) and the
    // outdoor door the SZ-1 charge blows open. Matched by Id, so other maps are untouched.
    private static readonly HashSet<string> KeylessDoors = new()
    {
        "door_Icebreaker_Indoor_01_00001", "door_Icebreaker_Indoor_01_00008", "door_Icebreaker_Indoor_01_00010",
        "door_Icebreaker_Indoor_01_00017", "door_Icebreaker_Indoor_01_00051", "door_Icebreaker_Indoor_01_00052",
        "door_Icebreaker_Indoor_01_00059", "door_Icebreaker_Indoor_01_00060", "door_Icebreaker_Indoor_02_00084",
        "door_Icebreaker_Outdoor_00000",
    };

    // Scene objects the mod finds by name (IcebreakerChainDoor, HatchMeltDriver); they are not WorldInteractiveObjects.
    private static readonly HashSet<string> NamedObjects = new()
    {
        "Icebreaker_chain_door", "Explosion_switch", "INTERACTIVE_Icebreaker_exterior_hatchway_door_frozen",
    };

    // Switches a mod completes a quest objective with instead of a zone. ManimalIcebreaker's IcebreakerPanelRepair binds
    // these three breaker panels to "Wiring the Vessel" VisitPlace targets fix_element_one / _02 / _03.
    private static readonly HashSet<string> QuestSwitches = new()
    {
        "switch_Icebreaker_Design_Stuff_00002", "switch_Icebreaker_Design_Stuff_00003", "switch_Icebreaker_Design_Stuff_00004",
    };

    private string DumpDir =>Path.Combine(Path.GetDirectoryName(Info.Location)!, "dumps");

    private void Awake()
    {
        // F10: unused by every other plugin config on the dev machine (F9 is FieldKit's "Toggle Chams").
        _dumpKey = Config.Bind("Dump", "Manual dump key", new KeyboardShortcut(KeyCode.F10),
            "Dump the current raid scene (modifier keys are ignored)");
        _autoDump = Config.Bind("Dump", "Auto dump", true, "Dump once per raid after the delay below");
        _autoDumpDelay = Config.Bind("Dump", "Auto dump delay (seconds)", 15f, "Time after entering a raid before the auto dump, so the scene can settle");
        Logger.LogInfo($"Zone dump loaded (press {_dumpKey.Value} in a raid, auto dump {(_autoDump.Value ? $"after {_autoDumpDelay.Value} s" : "off")}), output: {DumpDir}");
    }

    private void Update()
    {
        var location = Singleton<GameWorld>.Instance?.MainPlayer?.Location;

        // Read the main key directly: KeyboardShortcut.IsDown() fails while any other modifier (Shift to sprint, Ctrl…)
        // is held, which is easy to do mid-raid and gives no feedback at all.
        if (Input.GetKeyDown(_dumpKey.Value.MainKey))
        {
            Logger.LogInfo($"Dump key {_dumpKey.Value.MainKey} pressed");
            if (string.IsNullOrEmpty(location)) Logger.LogWarning("Not in a raid, nothing to dump");
            else Dump(location!);
            return;
        }

        if (string.IsNullOrEmpty(location))
        {
            // Out of raid: the next raid (even on the same map) gets its own auto dump.
            _raidSeenAt = -1f;
            _autoDumpedLocation = null;
            return;
        }

        // Auto dump once per raid, after the scene has had time to settle.
        if (!_autoDump.Value || _autoDumpedLocation == location) return;
        if (_raidSeenAt < 0f) _raidSeenAt = Time.time;
        if (Time.time - _raidSeenAt < _autoDumpDelay.Value) return;

        _autoDumpedLocation = location;
        Logger.LogInfo($"Auto dump {_autoDumpDelay.Value} s after entering {location}");
        Dump(location!);
    }

    private void Dump(string location)
    {
        try
        {
            // includeInactive: zones that only switch on under some quest state still exist in the scene.
            var zones = FindObjectsOfType<TriggerWithId>(true)
                .Select(t => new ZoneRow
                {
                    Id = t.Id,
                    Type = t.GetType().Name,
                    Active = t.gameObject.activeInHierarchy,
                    Position = V(t.transform.position),
                    Bounds = BoundsOf(t.gameObject),
                    Colliders = CollidersOf(t.gameObject),
                })
                // LaunchFlare conditions are judged by a separate component, not a TriggerWithId.
                .Concat(FindObjectsOfType<FlareShootDetectorZone>(true)
                    .Select(f => new ZoneRow
                    {
                        Id = f.zoneID,
                        Type = nameof(FlareShootDetectorZone),
                        Active = f.gameObject.activeInHierarchy,
                        Position = V(f.transform.position),
                        Bounds = BoundsOf(f.gameObject),
                        Colliders = CollidersOf(f.gameObject),
                    }))
                .OrderBy(z => z.Id)
                .ToList();

            var doors = FindObjectsOfType<WorldInteractiveObject>(true)
                .Where(d => !string.IsNullOrEmpty(d.KeyId) || KeylessDoors.Contains(d.Id))
                .Select(d => new DoorRow
                {
                    Id = d.Id,
                    KeyId = d.KeyId,
                    Type = d.GetType().Name,
                    Operatable = d.Operatable,
                    Position = V(d.transform.position),
                })
                .OrderBy(d => d.KeyId)
                .ToList();

            var switches = FindObjectsOfType<WorldInteractiveObject>(true)
                .Where(w => QuestSwitches.Contains(w.Id))
                .Select(w => new NamedRow { Name = w.Id, Active = w.gameObject.activeInHierarchy, Position = V(w.transform.position) })
                .OrderBy(n => n.Name)
                .ToList();

            var named = FindObjectsOfType<Transform>(true)
                .Where(t => NamedObjects.Contains(t.name))
                .Select(t => new NamedRow { Name = t.name, Active = t.gameObject.activeInHierarchy, Position = V(t.position) })
                .OrderBy(n => n.Name)
                .ToList();

            // Settings.Name is the server's allExtracts / secretExits Name. includeInactive: exits not rolled for this raid still exist.
            var exits = FindObjectsOfType<ExfiltrationPoint>(true)
                .Select(e => new ExitRow
                {
                    Name = e.Settings?.Name ?? "",
                    Type = e.GetType().Name,
                    Active = e.gameObject.activeInHierarchy,
                    Position = V(e.transform.position),
                    Bounds = BoundsOf(e.gameObject),
                })
                .OrderBy(e => e.Name)
                .ToList();

            // parameters.id is the server's base.transits id.
            var transits = FindObjectsOfType<TransitPoint>(true)
                .Select(t => new TransitRow
                {
                    Id = t.parameters?.id ?? -1,
                    Location = t.parameters?.location ?? "",
                    Active = t.gameObject.activeInHierarchy,
                    Position = V(t.transform.position),
                    Bounds = BoundsOf(t.gameObject),
                })
                .OrderBy(t => t.Id)
                .ToList();

            var dump = new DumpFile
            {
                Location = location,
                DumpedAtUtc = DateTime.UtcNow.ToString("o"),
                Zones = zones,
                Doors = doors,
                Exits = exits,
                Transits = transits,
                Named = named,
                Switches = switches,
            };

            Directory.CreateDirectory(DumpDir);
            // The game reports some ids capitalized (Sandbox, RezervBase); the server's locations folder is lowercase.
            var path = Path.Combine(DumpDir, $"{location.ToLowerInvariant()}.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(dump, Formatting.Indented));
            Logger.LogInfo($"Dumped {location}: {zones.Count} zones, {doors.Count} locked doors, {exits.Count} exits, {transits.Count} transits, {named.Count} named, {switches.Count} switches -> {path}");
        }
        catch (Exception e)
        {
            Logger.LogError($"Dump failed for {location}: {e}");
        }
    }

    private static Vec V(Vector3 v) => new() { X = v.x, Y = v.y, Z = v.z };

    private static BoundsRow? BoundsOf(GameObject go)
    {
        var collider = go.GetComponent<Collider>();
        if (collider == null) return null;
        var b = collider.bounds;
        return new BoundsRow { Min = V(b.min), Max = V(b.max) };
    }

    private static Quat Q(Quaternion q) => new() { X = q.x, Y = q.y, Z = q.z, W = q.w };

    /// <summary>
    /// Every collider on the zone object, in world space. Bounds above is only their axis-aligned envelope, which over-states
    /// rotated boxes (e.g. a Streets kill zone drawn over the road). For a BoxCollider this records the real box:
    /// centre (local centre transformed to world), size (local size times lossyScale) and the object's rotation.
    /// Other collider types keep only their type and world bounds.
    /// </summary>
    private static List<ColliderRow> CollidersOf(GameObject go)
        => go.GetComponents<Collider>().Select(c =>
        {
            var row = new ColliderRow { Type = c.GetType().Name, Min = V(c.bounds.min), Max = V(c.bounds.max) };
            if (c is BoxCollider box)
            {
                var t = box.transform;
                row.Center = V(t.TransformPoint(box.center));
                row.Size = V(Vector3.Scale(box.size, t.lossyScale));
                row.Rotation = Q(t.rotation);
                row.Yaw = t.rotation.eulerAngles.y;
            }
            return row;
        }).ToList();

    private class DumpFile
    {
        public string Location = "";
        public string DumpedAtUtc = "";
        public List<ZoneRow> Zones = new();
        public List<DoorRow> Doors = new();
        public List<ExitRow> Exits = new();
        public List<TransitRow> Transits = new();
        public List<NamedRow> Named = new();
        public List<NamedRow> Switches = new();
    }

    private class NamedRow
    {
        public string Name = "";
        public bool Active;
        public Vec Position = new();
    }

    private class ExitRow
    {
        public string Name = "";
        public string Type = "";
        public bool Active;
        public Vec Position = new();
        public BoundsRow? Bounds;
    }

    private class TransitRow
    {
        public int Id;
        public string Location = "";
        public bool Active;
        public Vec Position = new();
        public BoundsRow? Bounds;
    }

    private class ZoneRow
    {
        public string Id = "";
        public string Type = "";
        public bool Active;
        public Vec Position = new();
        public BoundsRow? Bounds;
        public List<ColliderRow> Colliders = new();
    }

    private class ColliderRow
    {
        public string Type = "";
        public Vec Min = new();
        public Vec Max = new();
        // BoxCollider only (null otherwise)
        public Vec? Center;
        public Vec? Size;
        public Quat? Rotation;
        public float? Yaw;
    }

    private class DoorRow
    {
        public string Id = "";
        public string KeyId = "";
        public string Type = "";
        public bool Operatable;
        public Vec Position = new();
    }

    private class BoundsRow
    {
        public Vec Min = new();
        public Vec Max = new();
    }

    private class Vec
    {
        public float X, Y, Z;
    }

    private class Quat
    {
        public float X, Y, Z, W;
    }
}
