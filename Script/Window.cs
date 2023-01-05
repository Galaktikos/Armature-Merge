#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.XR;

namespace Galaktikos.ArmatureMerge
{
    public class Window : EditorWindow
    {
        private Transform MainArmatureRoot;
        private Transform MergeArmatureRoot;
        private List<SkinnedMeshRenderer> MergeMeshes;
        private ExtraBonesActionType ExtraBonesAction;
        private bool RemoveUnusedBones;
        private bool IgnoreBonePath;

        private enum ExtraBonesActionType { None, Move, Constrain }

        [MenuItem("Window/Galaktikos/Armature Merge")]
        private static void Init()
        {
            Window window = (Window)GetWindow(typeof(Window));
            window.titleContent = new GUIContent("Armature Merge");
            window.Show();
        }

        private void OnGUI()
        {
            MainArmatureRoot = (Transform)EditorGUILayout.ObjectField("Main armature root", MainArmatureRoot, typeof(Transform), true);
            MergeArmatureRoot = (Transform)EditorGUILayout.ObjectField("Merge armature root", MergeArmatureRoot, typeof(Transform), true);
            ExtraBonesAction = (ExtraBonesActionType)EditorGUILayout.EnumPopup("Extra bones action", ExtraBonesAction);
            RemoveUnusedBones = EditorGUILayout.Toggle("Remove unused bones", RemoveUnusedBones);
            IgnoreBonePath = EditorGUILayout.Toggle("Ignore bone path", IgnoreBonePath);
            GUILayout.Space(20);

            if (MergeMeshes == null)
                MergeMeshes = new List<SkinnedMeshRenderer>();

            int newCount = Mathf.Max(0, EditorGUILayout.IntField("Merge Meshes", MergeMeshes.Count));
            while (newCount < MergeMeshes.Count)
                MergeMeshes.RemoveAt(MergeMeshes.Count - 1);
            while (newCount > MergeMeshes.Count)
                MergeMeshes.Add(null);

            for (int i = 0; i < MergeMeshes.Count; i++)
                MergeMeshes[i] = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(MergeMeshes[i], typeof(SkinnedMeshRenderer), true);

            GUILayout.Space(20);

            if (GUILayout.Button("Merge"))
            {
                Debug.Log(GetPathToArmature(MergeArmatureRoot, MainArmatureRoot));

                // Input Validation
                if (MainArmatureRoot == null || MergeArmatureRoot == null)
                {
                    Debug.LogWarning("Must provide both armatures");
                    return;
                }

                if (MergeMeshes.Count <= 0)
                {
                    Debug.LogWarning("Must have at least one new mesh");
                    return;
                }

                foreach (SkinnedMeshRenderer mesh in MergeMeshes)
                    if (mesh == null)
                    {
                        Debug.LogWarning("New meshes cannot be empty");
                        return;
                    }

                // Warnings
                if (!MainArmatureRoot.localScale.Equals(MergeArmatureRoot.localScale))
                    Debug.LogWarning("Armature scales are not equal, scaling issues may occur");


                Dictionary<Transform, Transform> matchedBonePairs = new Dictionary<Transform, Transform>();
                List<Transform> unmatchedBones = new List<Transform>();

                // Remap bones
                uint remapped = 0;
                uint unmatched = 0;
                foreach (SkinnedMeshRenderer mergeMesh in MergeMeshes)
                {
                    Transform foundRoot = null;
                    if (mergeMesh.rootBone != null)
                        foundRoot = IgnoreBonePath ? FindChildRecursive(MainArmatureRoot, mergeMesh.rootBone.name) : MainArmatureRoot.Find(GetPathToArmature(mergeMesh.rootBone, MergeArmatureRoot));

                    mergeMesh.rootBone = foundRoot == null ? MainArmatureRoot : foundRoot;

                    Transform[] mergeBones = new Transform[mergeMesh.bones.Length];
                    for (int boneIndex = 0; boneIndex < mergeBones.Length; boneIndex++)
                    {
                        Transform bone = mergeMesh.bones[boneIndex];
                        Transform found = (IgnoreBonePath) ? FindChildRecursive(MainArmatureRoot, bone.name) : MainArmatureRoot.Find(GetPathToArmature(bone, MergeArmatureRoot));

                        if (found == null)
                        {
                            mergeBones[boneIndex] = bone;
                            if (!unmatchedBones.Contains(bone))
                                unmatchedBones.Add(bone);

                            unmatched++;
                            continue;
                        }

                        mergeBones[boneIndex] = found;
                        if (!matchedBonePairs.ContainsKey(bone))
                            matchedBonePairs.Add(bone, found);

                        remapped++;
                    }

                    mergeMesh.bones = mergeBones;
                }

                // Extra bones action
                uint extraAction = 0;
                if (ExtraBonesAction != ExtraBonesActionType.None)
                    foreach (Transform bone in unmatchedBones)
                    {
                        KeyValuePair<Transform, Transform>? foundPair = FindMatchedParentRecursive(bone, matchedBonePairs);
                        if (foundPair == null)
                            continue;

                        switch (ExtraBonesAction)
                        {
                            case ExtraBonesActionType.Move:
                                bone.parent = foundPair.Value.Value;
                                break;

                            case ExtraBonesActionType.Constrain:
                                if (foundPair.Value.Key.gameObject.GetComponent<ParentConstraint>() != null)
                                    break;

                                ParentConstraint constraint = foundPair.Value.Key.gameObject.AddComponent<ParentConstraint>();
                                constraint.constraintActive = true;
                                constraint.weight = 1;
                                constraint.locked = true;
                                constraint.translationAxis = Axis.X | Axis.Y | Axis.Z;
                                constraint.rotationAxis = Axis.X | Axis.Y | Axis.Z;
                                constraint.AddSource(new ConstraintSource()
                                {
                                    sourceTransform = foundPair.Value.Value,
                                    weight = 1
                                });
                                break;
                        }

                        extraAction++;
                    }

                // Remove unused bones
                uint removed = 0;
                if (RemoveUnusedBones)
                    foreach (KeyValuePair<Transform, Transform> bonePair in matchedBonePairs)
                        if (bonePair.Key != null && !RemoveCheckRecursive(bonePair.Key, unmatchedBones.ToArray()))
                        {
                            DestroyImmediate(bonePair.Key.gameObject);
                            removed++;
                        }

                Debug.Log($"Armature merged ({remapped} remapped, {unmatched} unmatched, {extraAction} extra, {removed} removed)");
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                Transform found = FindChildRecursive(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static string GetPathToArmature(Transform bone, Transform armature)
        {
            if (bone.parent == null)
                return null;

            if (bone.parent == armature)
                return bone.name;

            string path = GetPathToArmature(bone.parent, armature);
            return path == null ? null : $"{path}/{bone.name}";
        }

        private KeyValuePair<Transform, Transform>? FindMatchedParentRecursive(Transform bone, Dictionary<Transform, Transform> matchedBonePairs)
        {
            if (bone == MergeArmatureRoot || bone.parent == null)
                return null;

            foreach (KeyValuePair<Transform, Transform> bonePair in matchedBonePairs)
                if (bone.parent == bonePair.Key)
                    return bonePair;

            return FindMatchedParentRecursive(bone.parent, matchedBonePairs);
        }

        private bool RemoveCheckRecursive(Transform bone, Transform[] excludeBones)
        {
            foreach (Transform child in bone)
            {
                foreach (Transform excludeBone in excludeBones)
                    if (child == excludeBone)
                        return true;

                if (RemoveCheckRecursive(child, excludeBones))
                    return true;
            }

            return false;
        }
    }
}
#endif