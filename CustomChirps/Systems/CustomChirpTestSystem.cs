using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

using Game.Buildings;            // Building
using CustomChirps.Systems;      // CustomChirpApiSystem, DepartmentAccount

namespace CustomChirps.Systems
{
    /// <summary>
    /// Simple test harness for the public API:
    /// - Rotates custom sender names (never shows vanilla dept name)
    /// - Rotates through DepartmentAccount enum to change the icon
    /// - Alternates messages with/without a building link
    /// </summary>
    [BurstCompile]
    public partial class CustomChirpTestSystem : SystemBase
    {
        private struct TestItem
        {
            public string Text;
            public string CustomSenderName;     // what the UI should show as sender label
            public DepartmentAccount Dept;      // which department icon to show
            public bool WithTarget;             // add clickable building link
        }

        private readonly List<TestItem> _plan = new List<TestItem>();
        private int _cursor;
        private float _timer;
        private Entity _anyBuilding;

        protected override void OnStartRunning()
        {
            var em = EntityManager;

            // Try to grab *any* building so we can test links
            using (var arr = em.CreateEntityQuery(ComponentType.ReadOnly<Building>())
                               .ToEntityArray(Allocator.Temp))
            {
                _anyBuilding = arr.Length > 0 ? arr[0] : Entity.Null;
            }

            // Custom sender names (what shows in the UI)
            var customNames = new[]
            {
                "Realistic Trips Mod",
                "Traffic AI+",
                "Skyline Stats",
                "Metro Manager",
                "Waste Watch",
                "City Life Kit",
            };

            // All department icons we’ll rotate through
            var depts = (DepartmentAccount[])Enum.GetValues(typeof(DepartmentAccount));

            // Build the plan: 12 messages total
            int nameIdx = 0;
            int deptIdx = 0;
            for (int i = 0; i < 12; i++)
            {
                var name = customNames[nameIdx % customNames.Length];
                var dep = depts[deptIdx % depts.Length];

                _plan.Add(new TestItem
                {
                    CustomSenderName = name,
                    Dept = dep,
                    // Note: The API will auto-append {LINK_1} if we pass a building and it's not present
                    Text = $"[#{i + 1}] Custom free text from '{name}'"
                                       + (i % 2 == 0 ? " (with target)" : " (no target)"),
                    WithTarget = (i % 2 == 0) && _anyBuilding != Entity.Null
                });

                nameIdx++;
                deptIdx++;
            }
        }

        protected override void OnUpdate()
        {
            if (_plan.Count == 0 || _cursor >= _plan.Count) return;

            // Emit one message every ~4s so UI is readable
            _timer += SystemAPI.Time.DeltaTime;
            if (_timer < 4f) return;
            _timer = 0f;

            var item = _plan[_cursor++];
            var target = item.WithTarget ? _anyBuilding : Entity.Null;

            //// One-call API: choose icon via DepartmentAccount, pass custom sender label, free text, and optional building
            //CustomChirpApiSystem.PostChirp(
            //    text: item.Text,
            //    department: item.Dept,
            //    building: target,
            //    customSenderName: item.CustomSenderName
            //);
        }
    }
}
