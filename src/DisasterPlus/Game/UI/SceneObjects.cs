using UnityEngine;

namespace DisasterPlus.Game
{
    /// <summary>
    /// Finds UI components in the scene. Main thread only.
    ///
    /// Object.FindObjectOfType is not used. On Unity 5.6 that API
    /// <b>does not return components on inactive GameObjects</b>. CS panels routinely sit in
    /// the state "constructed but not active yet", so a mod that goes looking at that moment
    /// decides "it is not there", with no guarantee it can fetch it again once the panel does
    /// become active (worst case, the feature silently never loads in that city).
    ///
    /// Resources.FindObjectsOfTypeAll&lt;T&gt;() returns inactive ones too (the generic form was
    /// confirmed to exist on Unity 5.6 by reflecting over the assembly).
    /// It does, however, also return prefabs and assets that do not belong to the scene, so
    /// narrow it to the real things in the scene with gameObject.scene.IsValid()
    /// (GameObject.scene / Scene.IsValid were likewise confirmed to exist).
    /// </summary>
    public static class SceneObjects
    {
        /// <summary>Returns one T from the scene, or null if none is found.</summary>
        public static T FindInScene<T>() where T : Component
        {
            var all = Resources.FindObjectsOfTypeAll<T>();
            if (all == null) return null;

            for (int i = 0; i < all.Length; i++)
            {
                T c = all[i];
                if (c == null) continue;   // also rejects Unity's fake null (destroyed)

                GameObject go = c.gameObject;
                if (go == null) continue;
                if (!go.scene.IsValid()) continue;   // comes from a prefab / asset

                return c;
            }
            return null;
        }
    }
}
