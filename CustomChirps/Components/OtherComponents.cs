// Components/HiddenChirp.cs
using Unity.Entities;

namespace CustomChirps.Components
{
    /// <summary>Tag: this chirp is suppressed by the vanilla gate.</summary>
    public struct HiddenChirp : IComponentData { }

    /// <summary>Tag: vanilla-gate has already made a keep/hide decision for this entity.</summary>
    public struct VanillaGateProcessed : IComponentData { }
}
