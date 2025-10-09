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
