using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using Mediapipe.Tasks.Vision.PoseLandmarker;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = UnityEngine.Vector3;
using MPNormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using System.ComponentModel;

public class NewBodyTracker : MonoBehaviour
{
    [Header("References")]
    public PoseLandmarkerRunner poseLandmarkerRunner;
    public Animator animator;

    [Header("Mirror Mode")]
    public bool mirror = false; // 前置摄像头开启镜像
    public bool flipY = true;

    [Header("Body Positioning")]
    public float heightOffset = -1f;             // 如果需要微调脚底高度
    public float forwardOffset = 0.0f;             // 模型相对于臀部中心的前后偏移（一般不需要）
    public float positionSmoothness = 15f;
    public float rotationSmoothness = 15f;
    public bool autoScale = true;                  // 是否根据肩宽自动缩放
    public float scaleSmoothness = 5f;
    [Range(0.1f, 20f)]
    public float bodyScale = 5f;                   // 手动缩放系数（autoScale关闭时使用）

    [Header("Virtual Scene Placement")]
    
    public bool enablePositionTracking = false; // 是否启用位置跟踪
    public bool faceCamera = false; // 是否让模型始终面向摄像头（仅在启用位置跟踪时有效）
    public Vector3 avatarInitialPosition = Vector3.zero; // 模型初始位置（如果不启用位置跟踪，模型将保持在此位置）
    public Vector3 avatarInitialRotation = new Vector3(0, 180, 0); // 模型初始旋转（如果不启用位置跟踪，模型将保持在此旋转）


    // 在 [Header("Body Positioning")] 下面添加
    public float depthMultiplier = 1f;   // 可在 Inspector 调
    public float depthOffset = 5f;         // 可在 Inspector 调

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

    public List<RotationMapping> rotationMappings = new List<RotationMapping>();

    private Dictionary<HumanBodyBones, Transform> boneIndexMap = new Dictionary<HumanBodyBones, Transform>();
    private Dictionary<HumanBodyBones, Vector3> boneInitLocalForward = new Dictionary<HumanBodyBones, Vector3>();
    private Dictionary<HumanBodyBones, Quaternion> boneInitLocalRot = new Dictionary<HumanBodyBones, Quaternion>();

    private List<MPNormalizedLandmark> currentLandmarks;
    private float targetScale = 1f;
    private float referenceShoulderWidth = 0.35f;  // 模型 T-Pose 时的肩膀宽度（世界单位）
    private Vector3 initialHipsOffset;             // 用于位置平滑

    void Start()
    {
        if (animator == null) animator = GetComponent<Animator>();

        InitializeBoneMaps();
        RecordInitialBoneDirections();

        // 记录模型参考肩宽（假设模型处于 T-Pose 或 A-Pose）
        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        if (leftShoulder && rightShoulder)
            referenceShoulderWidth = Vector3.Distance(leftShoulder.position, rightShoulder.position);
        else
            referenceShoulderWidth = 0.35f; // 默认值

        targetScale = bodyScale;
        initialHipsOffset = transform.position;
    }

    void Update()
    {
        if (poseLandmarkerRunner != null)
            UpdateBodyTracking();
        else
            Debug.LogError("PoseLandmarkerRunner not found.");
    }

    /// <summary>
    /// 深拷贝 landmarks，防止异步线程修改导致越界
    /// </summary>
    private List<MPNormalizedLandmark> GetLandmarksSnapshot()
    {
        var result = poseLandmarkerRunner.latestResult;
        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0) return null;
        var landmarks = result.poseLandmarks[0].landmarks;
        if (landmarks == null || landmarks.Count < 33) return null;
        return new List<MPNormalizedLandmark>(landmarks);
    }

    void UpdateBodyTracking()
    {
        var landmarks = GetLandmarksSnapshot();
        if (landmarks == null) return;
        currentLandmarks = landmarks;

        // 如果开启镜像，左右交换关键点（数据源交换，之后映射索引保持不变）


        // 1. 动态调整身体缩放
        if (autoScale)
        {
            UpdateAutoScale(landmarks);
        }
        else
        {
            targetScale = bodyScale;
            transform.localScale = Vector3.Lerp(transform.localScale, Vector3.one * targetScale, Time.deltaTime * scaleSmoothness);
        }

        if (enablePositionTracking)
        {
            // 2. 设置模型位置（臀部中心）
            Vector3 hipWorldPos = CalculateHipsWorldPosition(landmarks);
            hipWorldPos.y += heightOffset;
            hipWorldPos += transform.forward * forwardOffset;
            transform.position = Vector3.Lerp(transform.position, hipWorldPos, Time.deltaTime * positionSmoothness);
        }
        else
        {
            // 保持模型在初始位置（只跟踪旋转）
            transform.position = avatarInitialPosition;
            Quaternion initialRot = Quaternion.Euler(avatarInitialRotation);
            transform.rotation = Quaternion.Slerp(transform.rotation, initialRot, Time.deltaTime * rotationSmoothness);
        }

        // 3. 设置模型朝向（始终面对摄像头/用户）
        if (enablePositionTracking)
        {
            UpdateModelFacing();
        }

        // 4. 应用骨骼旋转
        foreach (var mapping in rotationMappings)
        {
            ApplyBoneRotationImproved(mapping, landmarks);
        }
    }


    // 根据肩宽动态计算 bodyScale
    void UpdateAutoScale(List<MPNormalizedLandmark> lm)
    {
        Vector3 leftShoulderWorld = GetWorldPointFromLandmark(lm[11]);
        Vector3 rightShoulderWorld = GetWorldPointFromLandmark(lm[12]);
        float detectedShoulderWidth = Vector3.Distance(leftShoulderWorld, rightShoulderWorld);
        if (detectedShoulderWidth < 0.01f) return;

        // 计算需要的全局缩放
        float desiredScale = detectedShoulderWidth / referenceShoulderWidth;
        targetScale = Mathf.Lerp(targetScale, desiredScale, Time.deltaTime * scaleSmoothness);
        transform.localScale = Vector3.one * targetScale;
    }
    /*
    // 让模型的正面始终朝向用户（即朝向摄像头）
    void UpdateModelFacing()
    {
        // 模型的正面（Z轴）指向摄像机的方向
        Vector3 dirToCamera = Camera.main.transform.position - transform.position;
        dirToCamera.y = 0; // 保持直立
        Quaternion targetRotation = Quaternion.LookRotation(dirToCamera);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothness);
    }
    */
    void UpdateModelFacing()
    {
        if (faceCamera)
        {
            Vector3 dirToCamera = Camera.main.transform.position - transform.position;
            dirToCamera.y = 0;
            Quaternion targetRotation = Quaternion.LookRotation(dirToCamera);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * rotationSmoothness);
        }
        else
        {
            // 用肩膀向量计算身体朝向
            Vector3 leftShoulder = GetWorldPointFromLandmark(currentLandmarks[11]);
            Vector3 rightShoulder = GetWorldPointFromLandmark(currentLandmarks[12]);
            Vector3 shoulderDir = (rightShoulder - leftShoulder).normalized;
            Vector3 bodyForward = Vector3.Cross(shoulderDir, Vector3.up); // 假设 Y 是上方
            if (bodyForward != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(bodyForward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSmoothness);
            }
        }
    }

    // 记录 T-Pose 下的骨骼局部前向和旋转
    // 替换原来的 RecordInitialBoneDirections
    void RecordInitialBoneDirections()
    {
        boneInitLocalForward.Clear();
        boneInitLocalRot.Clear();
        foreach (var map in rotationMappings)
        {
            Transform bone = animator.GetBoneTransform(map.bone);
            if (bone == null || bone.childCount == 0) continue;

            Transform child = bone.GetChild(0);  // 假设第一个子物体是下一关节
            Transform boneParent = bone.parent;

            Vector3 worldDir = (child.position - bone.position).normalized;
            // 关键修复：统一记录在「父骨骼局部空间」
            Vector3 initLocalDirInParentSpace = (boneParent != null) 
                ? boneParent.InverseTransformDirection(worldDir) 
                : worldDir;

            boneInitLocalForward[map.bone] = initLocalDirInParentSpace;
            boneInitLocalRot[map.bone] = bone.localRotation;
        }
    }
    /*
    // 改进的骨骼旋转（基于初始姿态的差值）
    // 替换原来的 ApplyBoneRotationImproved（逻辑不变，只是空间现在一致了）
    void ApplyBoneRotationImproved(RotationMapping mapping, List<MPNormalizedLandmark> landmarks)
    {
        if (!boneIndexMap.TryGetValue(mapping.bone, out Transform bone))
            return;

        int p = mapping.parentLandmarkIndex;
        int c = mapping.childLandmarkIndex;
        if (p >= landmarks.Count || c >= landmarks.Count) return;

        Vector3 parentPos = GetWorldPointFromLandmark(landmarks[p]);
        Vector3 childPos = GetWorldPointFromLandmark(landmarks[c]);
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

        bone.localRotation = Quaternion.Slerp(bone.localRotation, targetLocalRot, Time.deltaTime * rotationSmoothness);
    }
    */

    // 世界坐标转换（从归一化地标）
    Vector3 GetWorldPointFromLandmark(MPNormalizedLandmark lm)
    {
        float viewportX = mirror ? 1f - lm.x : lm.x;      // 注意：如果已经镜像过，这里就不用再翻转
        float viewportY = flipY ? 1f - lm.y : lm.y;
        float depth = lm.z * depthMultiplier + depthOffset;   // 你可以根据实际深度范围调整
        //float depth = depthOffset;
        Vector3 viewportPoint = new Vector3(viewportX, viewportY, depth);
        return Camera.main.ViewportToWorldPoint(viewportPoint);
    }

    Vector3 CalculateHipsWorldPosition(List<MPNormalizedLandmark> lm)
    {
        Vector3 leftHip = GetWorldPointFromLandmark(lm[23]);
        Vector3 rightHip = GetWorldPointFromLandmark(lm[24]);
        return (leftHip + rightHip) * 0.5f;
    }

    // IK 回调里也应用骨骼旋转（保持一致性）
    void OnAnimatorIK()
    {
        if (currentLandmarks == null || currentLandmarks.Count < 25) return;
        foreach (var mapping in rotationMappings)
            ApplyBoneRotationImproved(mapping, currentLandmarks);
    }

    // 构建骨骼映射表
    void InitializeBoneMaps()
    {
        boneIndexMap.Clear();
        rotationMappings.Clear();

        // 躯干和四肢映射（左右仍然对应原始索引，镜像后会自动映射）
        AddRotationMapping(HumanBodyBones.LeftUpperArm, 11, 13);
        AddRotationMapping(HumanBodyBones.RightUpperArm, 12, 14);
        AddRotationMapping(HumanBodyBones.LeftLowerArm, 13, 15);
        AddRotationMapping(HumanBodyBones.RightLowerArm, 14, 16);
        AddRotationMapping(HumanBodyBones.LeftUpperLeg, 23, 25);
        AddRotationMapping(HumanBodyBones.RightUpperLeg, 24, 26);
        AddRotationMapping(HumanBodyBones.LeftLowerLeg, 25, 27);
        AddRotationMapping(HumanBodyBones.RightLowerLeg, 26, 28);
        // 可以添加更多：脊柱、头等
        // ---- 躯干与头部 ----
        // 骨盆：髋中点 -> 肩中点 (控制整个身体方向)
        AddMidpointMapping(HumanBodyBones.Hips,
            parentA: 23, parentB: 24,          // 髋中心
            childA: 11, childB: 12);           // 肩中心

        // 脊柱：髋中点 -> 肩中点 (跟上一条类似，但 Humanoid 层级不同可分别控制)
        AddMidpointMapping(HumanBodyBones.Spine,
            parentA: 23, parentB: 24,
            childA: 11, childB: 12);

        // 胸部：肩中点 -> 鼻子 (上躯干前倾/后仰)
        AddMidpointMapping(HumanBodyBones.Chest,
            parentA: 11, parentB: 12,          // 肩中心
            childA: 0, childB: 0,              // 鼻子当单个点
            childIsMidpoint: false);           // 新增一个重载

        // 颈部：鼻子 -> 两耳中点
        AddMidpointMapping(HumanBodyBones.Neck,
            parentA: 0, parentB: 0, parentIsMidpoint: false,
            childA: 7, childB: 8);

        // 头部：两耳中点 -> 头顶(用鼻子代替，方向近似)
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

        Transform t = animator.GetBoneTransform(bone);
        if (t != null && !boneIndexMap.ContainsKey(bone))
            boneIndexMap[bone] = t;
    }

    void AddMidpointMapping(HumanBodyBones bone,
        int parentA, int parentB, int childA, int childB,
        bool parentIsMidpoint = true, bool childIsMidpoint = true)
    {
        var map = new RotationMapping {
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
        Transform t = animator.GetBoneTransform(bone);
        if (t != null && !boneIndexMap.ContainsKey(bone))
            boneIndexMap[bone] = t;
    }

    void ApplyBoneRotationImproved(RotationMapping mapping, List<MPNormalizedLandmark> landmarks)
    {
        if (!boneIndexMap.TryGetValue(mapping.bone, out Transform bone))
            return;

        // 获取父点世界坐标
        Vector3 parentPos;
        if (mapping.useParentMidpoint)
        {
            parentPos = (GetWorldPointFromLandmark(landmarks[mapping.parentMidpointA]) +
                        GetWorldPointFromLandmark(landmarks[mapping.parentMidpointB])) * 0.5f;
        }
        else
        {
            if (mapping.parentLandmarkIndex >= landmarks.Count) return;
            parentPos = GetWorldPointFromLandmark(landmarks[mapping.parentLandmarkIndex]);
        }

        // 获取子点世界坐标
        Vector3 childPos;
        if (mapping.useChildMidpoint)
        {
            childPos = (GetWorldPointFromLandmark(landmarks[mapping.childMidpointA]) +
                        GetWorldPointFromLandmark(landmarks[mapping.childMidpointB])) * 0.5f;
        }
        else
        {
            if (mapping.childLandmarkIndex >= landmarks.Count) return;
            childPos = GetWorldPointFromLandmark(landmarks[mapping.childLandmarkIndex]);
        }

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

        bone.localRotation = Quaternion.Slerp(bone.localRotation, targetLocalRot, Time.deltaTime * rotationSmoothness);
    }
}