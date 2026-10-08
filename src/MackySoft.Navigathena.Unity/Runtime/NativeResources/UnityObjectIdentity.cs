using UnityEngine;

#if UNITY_6000_4_OR_NEWER
using ObjectId = UnityEngine.EntityId;
#else
using ObjectId = System.Int32;
#endif

namespace MackySoft.Navigathena.Unity.NativeResources
{
    internal static class UnityObjectIdentity
    {
        internal static ObjectId Get (Object value)
        {
#if UNITY_6000_4_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }
    }
}
