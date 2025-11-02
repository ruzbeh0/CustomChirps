# Custom Chirps – Bridge API for Modders

**Custom Chirps** lets your mod post custom messages to the in-game Chirper with:

* custom free text
* a clickable link to any in-world entity (building, park, venue, etc.)
* a custom sender label
* a department icon (chosen from a fixed set of departments)

This integration uses a **reflection bridge**. If Custom Chirps isn’t installed, your mod will simply skip sending messages (no hard dependency).

See a working integration in the **Realistic Trips** mod (link below).

---

## Quick start

### 1. Add the bridge file to your mod

Create a `CustomChirpsBridge.cs` in your project.

See an example [here](https://github.com/ruzbeh0/Time2Work/blob/master/NightShift/Bridge/CustomChirpsBridge.cs)


---

### 2. Post a chirp (entity target)

Use an entity if you want a clickable link (camera focuses on this entity).

```csharp
using Unity.Entities;

// Ensure the bridge namespace matches the file you added
using CustomChirpsBridgeNamespace;

public static class Example
{
    public static void SendWithEntity(EntityManager em)
    {
        if (!CustomChirpsBridge.IsAvailable) return;

        // Pick any entity in your mod (e.g., a park, a venue, a placed building).
        Entity target = Entity.Null;
        using (var arr = em.CreateEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>())
                           .ToEntityArray(Unity.Collections.Allocator.Temp))
        {
            if (arr.Length > 0) target = arr[0];
        }

        CustomChirpsBridge.PostChirp(
            text: "Custom message with a clickable link.",
            department: DepartmentAccountBridge.Transportation,
            entity: target,
            customSenderName: "My Other Mod"
        );
    }
}
```

---

### Notes

* If you provide a target entity (or a prefab that resolves to an entity), and your text does not already contain a `{LINK_*}` token, `{LINK_1}` is automatically appended so the message becomes clickable.
* `customSenderName` controls the visible sender label in the feed.

---

## Department icons

Pick a department to control the **message icon**.
These names map 1:1 to the underlying department accounts in the game:

* Electricity
* FireRescue
* Roads
* Water
* Communications
* Police
* PropertyAssessmentOffice
* Post
* BusinessNews
* CensusBureau
* ParkAndRec
* EnvironmentalProtectionAgency
* Healthcare
* LivingStandardsAssociation
* Garbage
* TourismBoard
* Transportation
* Education

In the bridge, these appear as the enum `DepartmentAccountBridge` (same names).

---

## Runtime behavior

* If **Custom Chirps is present**, the bridge reflects the API and posts your message.
* If **Custom Chirps is not present**, `CustomChirpsBridge.IsAvailable` is `false` and your calls are safely skipped.

---

Here’s the same idea, laid out step-by-step so other modders can follow the pattern quickly.

---

# How to localize chirps (simple, self-contained pattern)

### 1) Make a tiny helper that resolves a key → localized text

It pulls the active dictionary from the game and replaces your `{placeholders}`.
If the key isn’t found, it safely falls back (so your game doesn’t blow up mid-chirp).

```csharp
// Utils/Loc.cs
using Colossal.Localization;
using Game.SceneFlow;

namespace SmartTransportation.Localization
{
    public static class T2WStrings
    {
        /// <summary>
        /// Translate a key using the active locale dictionary.
        /// Replaces {name} placeholders with provided values.
        /// Falls back to the key itself if missing.
        /// </summary>
        public static string T(string key, params (string name, string value)[] vars)
        {
            var mgr = GameManager.instance?.localizationManager;

            try
            {
                if (mgr?.activeDictionary != null &&
                    mgr.activeDictionary.TryGetValue(key, out var localized))
                {
                    if (vars != null)
                        foreach (var (n, v) in vars)
                            localized = localized.Replace("{" + n + "}", v ?? "");
                    return localized;
                }
            }
            catch { /* ignore and fallback */ }

            // safe fallback
            return key;
        }

        /// <summary>
        /// Current locale ID (e.g. "en-US", "pt-BR").
        /// </summary>
        public static string CurrentLocaleId()
        {
            try
            {
                return GameManager.instance?.localizationManager?.activeLocaleId ?? "en-US";
            }
            catch
            {
                return "en-US";
            }
        }
    }
}
```

---

### 2) Build your localized strings right before posting the chirp

You compose small pieces (like a line label) and then your full message.
Note: `{LINK_1}` is the clickable token; if you omit it and pass a target entity, Custom Chirps will auto-append it at the end.

```csharp
// Compose a localized "line label" part: "Bus Line 5", etc.
string lineLabel = T2WStrings.T(
    "chirp.line_label",
    ("type", transportLineData.m_TransportType.ToString()),
    ("number", routeNumber.m_Number.ToString())
);

// Compose the final localized message
string msg = T2WStrings.T(
    "chirp.stop_busy",
    ("line_label", lineLabel),
    ("waiting", maxStopWaiting.ToString())
);

// Post via the bridge
CustomChirpsBridge.PostChirp(
    text: msg,
    department: DepartmentAccountBridge.Transportation,
    entity: busiestStop,                          // clickable entity
    customSenderName: T2WStrings.T("chirp.mod_name")
);
```

---

### 3) Keep your keys in your own dictionary (any format you like)

Here’s the minimal set used above:

```text
{ "chirp.mod_name",   "SmartTransportation Mod" }
{ "chirp.line_label", "{type} Line {number}" }

{ "chirp.stop_busy",
  "{line_label}: Stop {LINK_1} is very busy — {waiting} passengers waiting." }
```

* You can store these in a JSON, a C# map, or any resource pipeline you prefer.
* Add more languages by providing another map per locale and choosing which one to use based on `T2WStrings.CurrentLocaleId()`.

---



