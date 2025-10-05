// Components/OverrideSender.cs
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Components
{
    public struct OverrideSender : IComponentData
    {
        public FixedString128Bytes Name;   // e.g., "Realistic Trips Mod"
        // (optional) keep using the account’s icon; add more fields later if you want a custom icon
    }
}
