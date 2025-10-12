using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Components
{
    // Attached to a chirp entity to carry runtime free text.
    public struct ModChirpText : IComponentData
    {
        public FixedString512Bytes Key;
    }
}
