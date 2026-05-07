using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;
using Unity.VisualScripting;

public class DancePlayback : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    [Header("Playback Data")]
    public DanceClipData clip; // 需要在 Inspector 中关联 DanceClipData 资源
    public Animator animator;
    public bool autoInitializeMappings = true;
    public List<RotationMapping> rotationMappings = new List<RotationMapping>(); // 需要在 Inspector 中设置每个landmark对应的骨骼和旋转轴
    [System.Serializable]
    public struct RotationMapping
    {
        public HumanBodyBones bone;
        public int parentLandmarkIndex;
        public int childLandmarkIndex;

        public bool useParentMidpoint; // 是否使用父骨骼和子骨骼的中点作为方向
        public bool useChildMidpoint;  // 是否使用子骨骼和父骨骼的中点作为方向
        public int parentMidpointA, parentMidpointB;
        public int childMidpointA, childMidpointB;

        public Vector3 axisOffset;
    }

    [Header("Playback Settings")]
    public float speedMultiplier = 1f;
    public bool playOnStart = false;

    [Header("Avatar Orientation")]
    public bool lockAvatarRotation = true;
    public Vector3 avatarInitialRotation = new Vector3(0, 180, 0);

    private float playbackTime = 0f;
    private bool isPlaying = false;

    private Dictionary<HumanBodyBones, Transform> boneIndexMap = new Dictionary<HumanBodyBones, Transform>();
    private Dictionary<HumanBodyBones, Vector3> boneInitLocalForward = new Dictionary<HumanBodyBones, Vector3>();
    private Dictionary<HumanBodyBones, Quaternion> boneInitLocalRot = new Dictionary<HumanBodyBones, Quaternion>();

    private List<NormalizedLandmark> constructedLandmarks = new List<NormalizedLandmark>(33);

    public bool IsPlaying => isPlaying;
    public float PlaybackTime => playbackTime;
    public float Duration => clip != null ? clip.Duration : 0f;
    public int[] RecordedIndices => clip != null ? clip.recordedIndices : new int[0];

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();
        ApplyAvatarOrientation();
        InitializeBoneMaps();
        RecordInitialBoneDirections();

        if (clip == null)
        {
            Debug.LogWarning("DancePlayback: No DanceClipData assigned. Please assign a clip to play.");
            return;
        }
        else
        {
            Debug.Log($"DancePlayback: Loaded clip with duration {clip.Duration:F2}s and {clip.frames.Count} frames.");
        }

        for (int i = 0; i < 33; i++)
            constructedLandmarks.Add(new NormalizedLandmark());

        if (playOnStart) Play();
    }

    void Update()
    {
        if (!isPlaying || clip == null) return;

        playbackTime += Time.deltaTime * speedMultiplier;
        if (playbackTime >= clip.Duration)
        {
            playbackTime = clip.Duration; // 停住，或循环
            // 也可触发结束事件
        }
        //Debug.Log($"Playback time: {playbackTime:F2}s / {Duration:F2}s");
        ApplyFrame(playbackTime);
    }

    /// <summary>
    /// 从某一时间点取出 landmark 数据并驱动模型。
    /// </summary>
    void ApplyFrame(float time)
    {
        //Debug.Log($"Applying frame at time {time:F2}s");
        FrameData frame = GetFrameAtTime(time);
        if (frame == null) return;

        ApplyAvatarOrientation();

        // 将录制的数据注入到完整的 landmark 列表中
        for (int i = 0; i < 33; i++)
            constructedLandmarks[i] = new NormalizedLandmark(); // 重置

        for (int i = 0; i < clip.recordedIndices.Length && i < frame.landmarkPositions.Length; i++)
        {
            int idx = clip.recordedIndices[i];
            Vector3 pos = frame.landmarkPositions[i];
            constructedLandmarks[idx] = new NormalizedLandmark(pos.x, pos.y, pos.z, 1f, null);
        }

        // 用与 NewBodyTracker 完全相同的方式驱动骨骼
        foreach (var mapping in rotationMappings)
        {
            ApplyBoneRotationImproved(mapping);
        }
    }

    private void ApplyAvatarOrientation()
    {
        if (!lockAvatarRotation) return;

        transform.rotation = Quaternion.Euler(avatarInitialRotation);
    }

    FrameData GetFrameAtTime(float time)
    {
        if (clip.frames == null || clip.frames.Count == 0) return null;
        // 简单最近邻查找，也可以线性插值
        FrameData best = clip.frames[0];
        for (int i = 1; i < clip.frames.Count; i++)
        {
            if (clip.frames[i].time > time) break;
            best = clip.frames[i];
        }
        return best;
    }

    // ---------- 骨骼驱动逻辑（与 NewBodyTracker 相同） ----------
    void InitializeBoneMaps()
    {
        boneIndexMap.Clear();
        if (autoInitializeMappings || rotationMappings.Count == 0)
            InitializeDefaultRotationMappings();

        foreach (var map in rotationMappings)
        {
            Transform t = animator.GetBoneTransform(map.bone);
            if (t != null && !boneIndexMap.ContainsKey(map.bone))
                boneIndexMap[map.bone] = t;
        }
    }

    void InitializeDefaultRotationMappings()
    {
        rotationMappings.Clear();

        AddRotationMapping(HumanBodyBones.LeftUpperArm, 11, 13);
        AddRotationMapping(HumanBodyBones.RightUpperArm, 12, 14);
        AddRotationMapping(HumanBodyBones.LeftLowerArm, 13, 15);
        AddRotationMapping(HumanBodyBones.RightLowerArm, 14, 16);
        AddRotationMapping(HumanBodyBones.LeftUpperLeg, 23, 25);
        AddRotationMapping(HumanBodyBones.RightUpperLeg, 24, 26);
        AddRotationMapping(HumanBodyBones.LeftLowerLeg, 25, 27);
        AddRotationMapping(HumanBodyBones.RightLowerLeg, 26, 28);

        AddMidpointMapping(HumanBodyBones.Hips,
            parentA: 23, parentB: 24,
            childA: 11, childB: 12);

        AddMidpointMapping(HumanBodyBones.Spine,
            parentA: 23, parentB: 24,
            childA: 11, childB: 12);

        AddMidpointMapping(HumanBodyBones.Chest,
            parentA: 11, parentB: 12,
            childA: 0, childB: 0,
            childIsMidpoint: false);

        AddMidpointMapping(HumanBodyBones.Neck,
            parentA: 0, parentB: 0, parentIsMidpoint: false,
            childA: 7, childB: 8);

        AddMidpointMapping(HumanBodyBones.Head,
            parentA: 7, parentB: 8,
            childA: 0, childB: 0, childIsMidpoint: false);
    }

    void AddRotationMapping(HumanBodyBones bone, int parentIdx, int childIdx)
    {
        rotationMappings.Add(new RotationMapping
        {
            bone = bone,
            parentLandmarkIndex = parentIdx,
            childLandmarkIndex = childIdx,
            axisOffset = Vector3.zero
        });
    }

    void AddMidpointMapping(HumanBodyBones bone,
        int parentA, int parentB, int childA, int childB,
        bool parentIsMidpoint = true, bool childIsMidpoint = true)
    {
        var map = new RotationMapping
        {
            bone = bone,
            axisOffset = Vector3.zero
        };

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

        rotationMappings.Add(map);
    }

    void RecordInitialBoneDirections()
    {
        boneInitLocalForward.Clear();
        boneInitLocalRot.Clear();
        foreach (var map in rotationMappings)
        {
            Transform bone = animator.GetBoneTransform(map.bone);
            if (bone == null || bone.childCount == 0) continue;

            Transform child = bone.GetChild(0);
            Transform boneParent = bone.parent;

            Vector3 worldDir = (child.position - bone.position).normalized;
            Vector3 initLocalDirInParentSpace = (boneParent != null)
                ? boneParent.InverseTransformDirection(worldDir)
                : worldDir;

            boneInitLocalForward[map.bone] = initLocalDirInParentSpace;
            boneInitLocalRot[map.bone] = bone.localRotation;
        }
    }

    void ApplyBoneRotationImproved(RotationMapping mapping)
    {
        if (!boneIndexMap.TryGetValue(mapping.bone, out Transform bone))
            return;

        // 获取父点世界坐标
        Vector3 parentPos;
        if (mapping.useParentMidpoint)
            parentPos = (GetWorldPoint(constructedLandmarks[mapping.parentMidpointA]) +
                         GetWorldPoint(constructedLandmarks[mapping.parentMidpointB])) * 0.5f;
        else
            parentPos = GetWorldPoint(constructedLandmarks[mapping.parentLandmarkIndex]);

        // 获取子点世界坐标
        Vector3 childPos;
        if (mapping.useChildMidpoint)
            childPos = (GetWorldPoint(constructedLandmarks[mapping.childMidpointA]) +
                        GetWorldPoint(constructedLandmarks[mapping.childMidpointB])) * 0.5f;
        else
            childPos = GetWorldPoint(constructedLandmarks[mapping.childLandmarkIndex]);

        Vector3 direction = (childPos - parentPos).normalized;
        if (direction == Vector3.zero) return;

        Transform boneParent = bone.parent;
        Vector3 localDir = boneParent != null
            ? boneParent.InverseTransformDirection(direction)
            : direction;

        Vector3 initForward = boneInitLocalForward.ContainsKey(mapping.bone)
            ? boneInitLocalForward[mapping.bone]
            : Vector3.forward;

        Quaternion fromRot = Quaternion.FromToRotation(initForward, localDir);
        Quaternion initRot = boneInitLocalRot.ContainsKey(mapping.bone)
            ? boneInitLocalRot[mapping.bone]
            : bone.localRotation;

        Quaternion targetLocalRot = fromRot * initRot;
        if (mapping.axisOffset != Vector3.zero)
            targetLocalRot *= Quaternion.Euler(mapping.axisOffset);

        bone.localRotation = Quaternion.Slerp(bone.localRotation, targetLocalRot, 1f); // 直接设置无需平滑
    }

    // 世界坐标转换（与 NewBodyTracker 相同逻辑，但后续可提取为公共方法）
    private Vector3 GetWorldPoint(NormalizedLandmark lm)
    {
        // 这里假设你的主摄像机与 NewBodyTracker 使用相同配置，可直接调用 Camera.main
        float viewportX = lm.x; // 录制时已存储原始值，如需镜像请在 NewBodyTracker 中保持一致处理
        float viewportY = lm.y;
        float depth = lm.z;     // 可根据需要加上 depthMultiplier/depthOffset
        Vector3 viewportPoint = new Vector3(viewportX, viewportY, depth);
        return Camera.main.ViewportToWorldPoint(viewportPoint);
    }

    // ---------- 外部控制方法 ----------
    public void Play()
    {
        Debug.Log("DancePlayback: Play called.");
        isPlaying = true;
        playbackTime = 0f;
    }

    public void Pause()
    {
        isPlaying = false;
    }

    public void Stop()
    {
        isPlaying = false;
        playbackTime = 0f;
    }

    public void Seek(float time)
    {
        playbackTime = Mathf.Clamp(time, 0f, Duration);
    }

    public List<NormalizedLandmark> GetCurrentLandmarks()
    {
        return constructedLandmarks;
    }
}
