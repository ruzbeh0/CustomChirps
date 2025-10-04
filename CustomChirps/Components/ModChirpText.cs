using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Entities;

namespace CustomChirps.Components
{
    // Attached to a chirp entity to carry runtime free text.
    public struct ModChirpText : IComponentData
    {
        public Unity.Collections.FixedString512Bytes Key;
    }
}
