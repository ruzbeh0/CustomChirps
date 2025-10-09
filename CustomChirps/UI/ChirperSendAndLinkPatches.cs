// UI/ChirperSenderAndLinkPatches.cs
using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Entities;
using Colossal.UI.Binding;                  // IJsonWriter
using Game.UI.InGame;                       // ChirperUISystem
using CustomChirps.Systems;                 // RuntimeChirpTextBus

namespace CustomChirps.UI
{
    /// <summary>
    /// Approach:
    ///  • BindChirpSender.Prefix: if THIS chirp has a payload, set a short-lived baton with the override name,
    ///    and enforce the sender account on the instance.
    ///  • BindChirpLink (resolved via TargetMethod): before vanilla writes the link JSON, replace the 'name'
    ///    parameter (boxed value) with a CustomName(string) instance built via reflection.
    /// This affects only the sender link of this chirp; baton is consumed immediately after.
    /// </summary>
    public static class ChirperSenderAndLinkPatches
    {
        // Short-lived baton, consumed by the next BindChirpLink call during this chirp render.
        // (UI runs on the main thread, so plain static is fine.)
        internal static string PendingSenderName;

        // -----------------------------------------------------------------------------------------
        // 1) BindChirpSender: lock sender entity and stage the display-name override for this chirp
        //    Signature: private void BindChirpSender(IJsonWriter binder, Entity entity)
        // -----------------------------------------------------------------------------------------
        [HarmonyPatch(typeof(ChirperUISystem), "BindChirpSender")]
        public static class BindChirpSender_Patch
        {
            public static void Prefix(IJsonWriter binder, Entity entity)
            {
                PendingSenderName = null;

                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null || entity == Entity.Null) return;

                var em = world.EntityManager;

                if (!em.Exists(entity)) return;

                // The payload is attached to the *chirp entity* (the 'entity' parameter here).
                if (RuntimeChirpTextBus.TryGetAttached(entity, out var payload))
                {
                    // Enforce sender (icon/account) on this instance if needed.
                    if (payload.Sender != Entity.Null && em.HasComponent<Game.Triggers.Chirp>(entity))
                    {
                        var c = em.GetComponentData<Game.Triggers.Chirp>(entity);
                        if (c.m_Sender != payload.Sender)
                        {
                            c.m_Sender = payload.Sender;
                            em.SetComponentData(entity, c);
                        }
                    }

                    // Prepare custom sender *text* for the immediately following BindChirpLink call.
                    var name = payload.OverrideSenderName.ToString(); // FixedString128 -> string
                    if (!string.IsNullOrWhiteSpace(name))
                        PendingSenderName = name;
                }
            }
            // We intentionally do not clear here; the next BindChirpLink consumes it.
        }

        // -----------------------------------------------------------------------------------------
        // 2) BindChirpLink: reflection-targeted overload with (IJsonWriter, Entity, NameSystem.Name)
        //    We replace the 'name' parameter via ref object to avoid compile-time dependency on NameSystem.
        // -----------------------------------------------------------------------------------------
        [HarmonyPatch]
        public static class BindChirpLink_Patch
        {
            // Resolve the correct overload dynamically:
            static MethodBase TargetMethod()
            {
                var type = typeof(ChirperUISystem);
                var methods = AccessTools.GetDeclaredMethods(type)
                    .Where(m => m.Name == "BindChirpLink")
                    .ToArray();

                // We need the 3-arg overload: (IJsonWriter, Entity, <NameSystem.Name>)
                foreach (var m in methods)
                {
                    var p = m.GetParameters();
                    if (p.Length == 3 &&
                        typeof(IJsonWriter).IsAssignableFrom(p[0].ParameterType) &&
                        p[1].ParameterType == typeof(Entity) &&
                        p[2].ParameterType.IsValueType &&     // Name is a struct
                        p[2].ParameterType.Name == "Name")     // nested struct 'Name'
                    {
                        return m;
                    }
                }

                // Fallback: find by parameter count/names if necessary
                return methods.FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    return p.Length == 3 &&
                           typeof(IJsonWriter).IsAssignableFrom(p[0].ParameterType) &&
                           p[1].ParameterType == typeof(Entity);
                });
            }

            // Replace the boxed 'name' value before vanilla writes it.
            // NOTE: we accept 'ref object name' so we can assign the new struct instance.
            public static void Prefix(IJsonWriter binder, Entity entity, ref object name)
            {
                if (string.IsNullOrEmpty(ChirperSenderAndLinkPatches.PendingSenderName))
                    return;

                try
                {
                    var nameBoxed = BuildCustomNameBoxed(name?.GetType(), ChirperSenderAndLinkPatches.PendingSenderName);
                    if (nameBoxed != null)
                    {
                        name = nameBoxed; // assign the new struct instance
                    }
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"[BindChirpLink_Patch] Failed to set custom name: {ex}");
                }
                finally
                {
                    // Consume so we only affect the sender link for this chirp
                    ChirperSenderAndLinkPatches.PendingSenderName = null;
                }
            }

            // Create a NameSystem.Name instance by calling its static CustomName(string) via reflection.
            static object BuildCustomNameBoxed(Type actualNameType, string text)
            {
                if (actualNameType == null) return null;

                // The nested struct type is something like Game.UI.Localization.NameSystem+Name
                // or possibly in another ns but still named 'Name'.
                // It should expose a static method CustomName(string).
                var customName = AccessTools.Method(actualNameType, "CustomName", new[] { typeof(string) });
                if (customName == null)
                {
                    // Some builds define CustomName on the declaring type instead (the NameSystem type).
                    var declaring = actualNameType.DeclaringType;
                    if (declaring != null)
                        customName = AccessTools.Method(declaring, "CustomName", new[] { typeof(string) });
                }

                if (customName == null)
                {
                    Mod.log.Warn("[BindChirpLink_Patch] Could not find CustomName(string) via reflection.");
                    return null;
                }

                return customName.Invoke(null, new object[] { text });
            }
        }
    }
}
