using System;
using UnityEditor;
using UnityEngine;
using WorldPvp.Phase1.Combat;

namespace WorldPvp.Phase1.Editor
{
    /// <summary>Creates the editable prototype WeaponDefinition asset used by the generated Phase 7 scene.</summary>
    internal static class PhaseSevenCombatAssetBuilder
    {
        internal const string PrototypeWeaponPath = "Assets/WorldPvp/Configuration/Phase7_PrototypeRifle.asset";

        internal static WeaponDefinition LoadOrCreatePrototypeRifle()
        {
            WeaponDefinition definition = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(PrototypeWeaponPath);
            if (definition != null)
            {
                return definition;
            }

            definition = ScriptableObject.CreateInstance<WeaponDefinition>();
            definition.name = "Phase7_PrototypeRifle";
            SerializedObject serialized = new SerializedObject(definition);
            SetString(serialized, "weaponId", "prototype_rifle");
            SetString(serialized, "displayName", "Prototype Rifle");
            SetFloat(serialized, "fireRateShotsPerSecond", 5f);
            SetFloat(serialized, "damage", 34f);
            SetFloat(serialized, "rangeMeters", 150f);
            SetFloat(serialized, "spreadDegrees", 0.45f);
            SetInt(serialized, "magazineCapacity", 30);
            SetFloat(serialized, "reloadTimeSeconds", 2.1f);
            SetFloat(serialized, "headDamageMultiplier", 2f);
            SetFloat(serialized, "torsoDamageMultiplier", 1f);
            SetFloat(serialized, "limbDamageMultiplier", 0.65f);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.CreateAsset(definition, PrototypeWeaponPath);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            return definition;
        }

        private static void SetString(SerializedObject target, string name, string value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("WeaponDefinition is missing serialized field: " + name);
            }
            property.stringValue = value;
        }

        private static void SetFloat(SerializedObject target, string name, float value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("WeaponDefinition is missing serialized field: " + name);
            }
            property.floatValue = value;
        }

        private static void SetInt(SerializedObject target, string name, int value)
        {
            SerializedProperty property = target.FindProperty(name);
            if (property == null)
            {
                throw new InvalidOperationException("WeaponDefinition is missing serialized field: " + name);
            }
            property.intValue = value;
        }
    }
}
