#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[CustomEditor(typeof(DancePlayback))]
public class DancePlaybackEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        DancePlayback dp = (DancePlayback)target;

        if (GUILayout.Button("Load Default Mappings"))
        {
            // 直接生成与 NewBodyTracker 完全相同的默认映射列表
            dp.rotationMappings = new List<DancePlayback.RotationMapping>();

            // 四肢
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.LeftUpperArm, 11, 13));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.RightUpperArm, 12, 14));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.LeftLowerArm, 13, 15));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.RightLowerArm, 14, 16));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.LeftUpperLeg, 23, 25));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.RightUpperLeg, 24, 26));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.LeftLowerLeg, 25, 27));
            dp.rotationMappings.Add(CreateMapping(HumanBodyBones.RightLowerLeg, 26, 28));

            // 躯干与头部（中点映射）
            dp.rotationMappings.Add(CreateMidpointMapping(HumanBodyBones.Hips, 23, 24, 11, 12));
            dp.rotationMappings.Add(CreateMidpointMapping(HumanBodyBones.Spine, 23, 24, 11, 12));
            dp.rotationMappings.Add(CreateMidpointMapping(HumanBodyBones.Chest, 11, 12, 0, 0, true, false)); // 注意 childIsMidpoint = false
            dp.rotationMappings.Add(CreateMidpointMapping(HumanBodyBones.Neck, 0, 0, 7, 8, false, true)); // parentIsMidpoint = false
            dp.rotationMappings.Add(CreateMidpointMapping(HumanBodyBones.Head, 7, 8, 0, 0, true, false)); // childIsMidpoint = false

            EditorUtility.SetDirty(dp);
            Debug.Log($"已加载默认映射（共 {dp.rotationMappings.Count} 条）");
        }
    }

    private DancePlayback.RotationMapping CreateMapping(HumanBodyBones bone, int parentIdx, int childIdx)
    {
        return new DancePlayback.RotationMapping
        {
            bone = bone,
            parentLandmarkIndex = parentIdx,
            childLandmarkIndex = childIdx,
            useParentMidpoint = false,
            useChildMidpoint = false,
            axisOffset = Vector3.zero
        };
    }

    private DancePlayback.RotationMapping CreateMidpointMapping(
        HumanBodyBones bone,
        int parentA, int parentB,
        int childA, int childB,
        bool parentIsMidpoint = true,
        bool childIsMidpoint = true)
    {
        var map = new DancePlayback.RotationMapping { bone = bone, axisOffset = Vector3.zero };
        if (parentIsMidpoint)
        {
            map.useParentMidpoint = true;
            map.parentMidpointA = parentA;
            map.parentMidpointB = parentB;
        }
        else
        {
            map.parentLandmarkIndex = parentA;
        }
        if (childIsMidpoint)
        {
            map.useChildMidpoint = true;
            map.childMidpointA = childA;
            map.childMidpointB = childB;
        }
        else
        {
            map.childLandmarkIndex = childA;
        }
        return map;
    }
}
#endif
