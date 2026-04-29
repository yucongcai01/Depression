using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using MPNormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

public class BodyTrackerFullBodyIK : MonoBehaviour
{
    [Header("References")]
    public PoseLandmarkerRunner poseLandmarkerRunner;
    public Animator animator;

    [Header("Input Mapping")]
    public bool mirrorInputX = false;
    public bool flipY = true;

    // 角色面对摄像机时，通常需要打开这个。
    public bool invertTrackingX = true;

    // 如果你举左手，模型右手动，再打开这个。
    public bool swapLeftRightLandmarks = false;

    [Header("Avatar Placement")]
    public bool lockAvatarTransform = true;
    public bool captureScenePoseOnStart = true;
    public Vector3 avatarInitialPosition = Vector3.zero;
    public Vector3 avatarInitialRotation = new Vector3(0f, 180f, 0f);

    [Header("Avatar Scale")]
    public float bodyScale = 1f;
    public float scaleSmoothness = 10f;

    [Header("Tracking Space")]
    public float trackingWidth = 2.2f;
    public float trackingHeight = 2.2f;
    public float trackingYOffset = 0f;

    // 注意：这是“视觉前方”的偏移，不一定等于 Unity local +Z。
    public float trackingVisualZOffset = 0f;

    public bool useLandmarkDepth = false;
    public float landmarkDepthScale = 0.4f;
    public float landmarkDepthSign = -1f;

    [Header("Avatar Direction Fix")]
    // 如果角色 Transform Y=180 后才面对你，通常这个要 true。
    public bool invertForwardForIK = true;

    [Header("Root Tracking for Dance")]
    public bool enableRootTracking = false;
    public float rootTrackingWidth = 1.2f;
    public float rootTrackingHeight = 0.8f;
    public float rootTrackingDepth = 0.5f;
    public float rootSmoothness = 8f;
    public float maxRootHorizontalOffset = 1.2f;
    public float maxRootVerticalOffset = 0.8f;

    [Header("Arm IK")]
    public bool armsEnabled = true;
    [Range(0f, 1f)] public float armIKWeight = 1f;
    [Range(0f, 1f)] public float elbowHintWeight = 1f;
    public float armTargetSmoothness = 18f;
    public float armReachMultiplier = 0.98f;

    [Header("Elbow Hint")]
    public float elbowHintSide = 0.45f;
    public float elbowHintForward = 0.3f;
    public float elbowHintDown = 0.1f;

    // 先设 0，等方向稳定后再加到 0.1~0.25。
    [Range(0f, 1f)] public float elbowLandmarkBlend = 0f;

    [Header("Hand Visual Z Clamp")]
    public bool clampHandVisualZ = true;
    public float minHandVisualZ = -0.05f;
    public float maxHandVisualZ = 1.1f;

    [Header("Leg IK")]
    public bool legsEnabled = true;
    [Range(0f, 1f)] public float legIKWeight = 0.8f;
    [Range(0f, 1f)] public float kneeHintWeight = 0.9f;
    public float legTargetSmoothness = 14f;
    public float legReachMultiplier = 0.98f;

    [Header("Knee Hint")]
    public float kneeHintSide = 0.12f;
    public float kneeHintForward = 0.45f;
    public float kneeHintDown = 0.45f;

    // 腿部 MediaPipe 抖动通常更明显，所以先小一点。
    [Range(0f, 1f)] public float kneeLandmarkBlend = 0.1f;

    [Range(0f, 1f)] public float kneeHintDynamicWeight = 0.7f;

    [Header("Foot Target")]
    public bool useFootIndexForFootTarget = false;
    public float footTargetYOffset = 0f;

    [Header("Debug")]
    public bool drawDebugTargets = true;
    public KeyCode recalibrateKey = KeyCode.C;

    private List<MPNormalizedLandmark> currentLandmarks;

    private Vector3 initialLocalScale;
    private Vector3 smoothedRootPosition;
    private bool hasCalibration = false;
    private Vector3 calibratedHipCenter01;

    private Transform hipsBone;

    private Transform leftUpperArm;
    private Transform leftLowerArm;
    private Transform leftHand;

    private Transform rightUpperArm;
    private Transform rightLowerArm;
    private Transform rightHand;

    private Transform leftUpperLeg;
    private Transform leftLowerLeg;
    private Transform leftFoot;

    private Transform rightUpperLeg;
    private Transform rightLowerLeg;
    private Transform rightFoot;

    private float leftArmLength;
    private float rightArmLength;
    private float leftLegLength;
    private float rightLegLength;

    private Vector3 leftHandTargetSmoothed;
    private Vector3 rightHandTargetSmoothed;
    private Vector3 leftElbowHintSmoothed;
    private Vector3 rightElbowHintSmoothed;

    private Vector3 leftFootTargetSmoothed;
    private Vector3 rightFootTargetSmoothed;
    private Vector3 leftKneeHintSmoothed;
    private Vector3 rightKneeHintSmoothed;

    private bool hasSmoothedTargets = false;

    private Vector3 debugLeftHandTarget;
    private Vector3 debugRightHandTarget;
    private Vector3 debugLeftElbowHint;
    private Vector3 debugRightElbowHint;
    private Vector3 debugLeftFootTarget;
    private Vector3 debugRightFootTarget;
    private Vector3 debugLeftKneeHint;
    private Vector3 debugRightKneeHint;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("BodyTrackerFullBodyIK: Animator not found.");
            enabled = false;
            return;
        }

        animator.applyRootMotion = false;

        if (captureScenePoseOnStart)
        {
            avatarInitialPosition = transform.position;
            avatarInitialRotation = transform.eulerAngles;
        }

        initialLocalScale = transform.localScale;
        smoothedRootPosition = avatarInitialPosition;

        CacheBones();
        CacheLimbLengths();

        if (hipsBone == null)
        {
            Debug.LogError("BodyTrackerFullBodyIK: Hips bone not found. Check Humanoid Avatar mapping.");
            enabled = false;
            return;
        }

        Debug.Log("BodyTrackerFullBodyIK initialized.");
        Debug.Log("LeftHand: " + leftHand);
        Debug.Log("RightHand: " + rightHand);
        Debug.Log("LeftFoot: " + leftFoot);
        Debug.Log("RightFoot: " + rightFoot);
    }

    void Update()
    {
        if (poseLandmarkerRunner == null)
        {
            Debug.LogError("BodyTrackerFullBodyIK: PoseLandmarkerRunner not assigned.");
            return;
        }

        var landmarks = GetLandmarksSnapshot();
        if (landmarks == null)
            return;

        currentLandmarks = landmarks;

        if (!hasCalibration || Input.GetKeyDown(recalibrateKey))
        {
            CalibrateTracking(landmarks);
        }

        UpdateAvatarTransform(landmarks);
        UpdateAvatarScale();
    }

    private List<MPNormalizedLandmark> GetLandmarksSnapshot()
    {
        var result = poseLandmarkerRunner.latestResult;

        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
            return null;

        var landmarks = result.poseLandmarks[0].landmarks;

        if (landmarks == null || landmarks.Count < 33)
            return null;

        return new List<MPNormalizedLandmark>(landmarks);
    }

    private void CalibrateTracking(List<MPNormalizedLandmark> landmarks)
    {
        calibratedHipCenter01 = GetHipCenter01(landmarks);
        hasCalibration = true;

        smoothedRootPosition = avatarInitialPosition;

        Debug.Log("BodyTrackerFullBodyIK: Calibration complete. Press C to recalibrate.");
    }

    private void UpdateAvatarTransform(List<MPNormalizedLandmark> landmarks)
    {
        if (!lockAvatarTransform)
            return;

        Quaternion targetRot = Quaternion.Euler(avatarInitialRotation);

        if (!enableRootTracking)
        {
            transform.SetPositionAndRotation(avatarInitialPosition, targetRot);
            return;
        }

        Vector3 hipNow = GetHipCenter01(landmarks);
        Vector3 delta = hipNow - calibratedHipCenter01;

        float dx = delta.x;
        if (invertTrackingX)
            dx = -dx;

        float dy = delta.y;

        float visualZ = 0f;
        if (useLandmarkDepth)
            visualZ = delta.z * rootTrackingDepth * landmarkDepthSign;

        Vector3 localOffset = new Vector3(
            dx * rootTrackingWidth,
            dy * rootTrackingHeight,
            ConvertVisualZToLocalZ(visualZ)
        );

        localOffset.x = Mathf.Clamp(localOffset.x, -maxRootHorizontalOffset, maxRootHorizontalOffset);
        localOffset.y = Mathf.Clamp(localOffset.y, -maxRootVerticalOffset, maxRootVerticalOffset);

        Vector3 targetPos = avatarInitialPosition + targetRot * localOffset;

        smoothedRootPosition = SmoothPoint(smoothedRootPosition, targetPos, rootSmoothness);

        transform.SetPositionAndRotation(smoothedRootPosition, targetRot);
    }

    private void UpdateAvatarScale()
    {
        Vector3 targetScale = initialLocalScale * bodyScale;
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            1f - Mathf.Exp(-scaleSmoothness * Time.deltaTime)
        );
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null || currentLandmarks == null || currentLandmarks.Count < 33)
        {
            ClearIKWeights();
            return;
        }

        if (armsEnabled)
        {
            UpdateArmIK(true);
            UpdateArmIK(false);
        }
        else
        {
            ClearArmIKWeights();
        }

        if (legsEnabled)
        {
            UpdateLegIK(true);
            UpdateLegIK(false);
        }
        else
        {
            ClearLegIKWeights();
        }
    }

    /*
    private void UpdateArmIK(bool avatarLeft)
    {
        int shoulderIndex = GetShoulderIndex(avatarLeft);
        int elbowIndex = GetElbowIndex(avatarLeft);
        int wristIndex = GetWristIndex(avatarLeft);

        Transform upperArm = avatarLeft ? leftUpperArm : rightUpperArm;
        Transform lowerArm = avatarLeft ? leftLowerArm : rightLowerArm;
        Transform hand = avatarLeft ? leftHand : rightHand;

        if (upperArm == null || lowerArm == null || hand == null)
            return;

        AvatarIKGoal goal = avatarLeft ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
        AvatarIKHint hint = avatarLeft ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;

        Vector3 shoulderWorld = upperArm.position;
        Vector3 wristWorld = GetBodyRelativeWorldPoint(currentLandmarks[wristIndex], currentLandmarks);
        Vector3 elbowWorld = GetBodyRelativeWorldPoint(currentLandmarks[elbowIndex], currentLandmarks);

        float maxReach = avatarLeft ? leftArmLength : rightArmLength;
        wristWorld = ClampToReach(shoulderWorld, wristWorld, maxReach * armReachMultiplier);

        if (clampHandVisualZ)
            wristWorld = ClampVisualZ(wristWorld, minHandVisualZ, maxHandVisualZ);

        Vector3 sideDir = avatarLeft ? -transform.right : transform.right;
        Vector3 forwardDir = GetAvatarVisualForward();

        Vector3 stableElbowHint =
            shoulderWorld
            + sideDir * elbowHintSide
            + forwardDir * elbowHintForward
            - transform.up * elbowHintDown;

        Vector3 elbowHintWorld = Vector3.Lerp(
            stableElbowHint,
            elbowWorld,
            elbowLandmarkBlend
        );

        if (!hasSmoothedTargets)
        {
            leftHandTargetSmoothed = wristWorld;
            rightHandTargetSmoothed = wristWorld;
            leftElbowHintSmoothed = elbowHintWorld;
            rightElbowHintSmoothed = elbowHintWorld;

            leftFootTargetSmoothed = leftFoot ? leftFoot.position : transform.position;
            rightFootTargetSmoothed = rightFoot ? rightFoot.position : transform.position;
            leftKneeHintSmoothed = transform.position;
            rightKneeHintSmoothed = transform.position;

            hasSmoothedTargets = true;
        }

        if (avatarLeft)
        {
            leftHandTargetSmoothed = SmoothPoint(leftHandTargetSmoothed, wristWorld, armTargetSmoothness);
            leftElbowHintSmoothed = SmoothPoint(leftElbowHintSmoothed, elbowHintWorld, armTargetSmoothness);

            animator.SetIKPositionWeight(goal, armIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, leftHandTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, elbowHintWeight);
            animator.SetIKHintPosition(hint, leftElbowHintSmoothed);

            debugLeftHandTarget = leftHandTargetSmoothed;
            debugLeftElbowHint = leftElbowHintSmoothed;
        }
        else
        {
            rightHandTargetSmoothed = SmoothPoint(rightHandTargetSmoothed, wristWorld, armTargetSmoothness);
            rightElbowHintSmoothed = SmoothPoint(rightElbowHintSmoothed, elbowHintWorld, armTargetSmoothness);

            animator.SetIKPositionWeight(goal, armIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, rightHandTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, elbowHintWeight);
            animator.SetIKHintPosition(hint, rightElbowHintSmoothed);

            debugRightHandTarget = rightHandTargetSmoothed;
            debugRightElbowHint = rightElbowHintSmoothed;
        }
    }
    */
    private void UpdateArmIK(bool avatarLeft)
    {
        int shoulderIndex = GetShoulderIndex(avatarLeft);
        int elbowIndex = GetElbowIndex(avatarLeft);
        int wristIndex = GetWristIndex(avatarLeft);

        Transform upperArm = avatarLeft ? leftUpperArm : rightUpperArm;
        Transform lowerArm = avatarLeft ? leftLowerArm : rightLowerArm;
        Transform hand = avatarLeft ? leftHand : rightHand;

        if (upperArm == null || lowerArm == null || hand == null)
            return;

        AvatarIKGoal goal = avatarLeft ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
        AvatarIKHint hint = avatarLeft ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;

        // 关键：在 IK 改手之前，先保存 Animator 原本给手的旋转。
        // 这样 IK 只负责把手放到 wrist target，手腕 roll 不再交给 Unity 自己乱猜。
        Quaternion stableHandRotation = hand.rotation;

        Vector3 shoulderWorld = upperArm.position;
        Vector3 wristWorld = GetBodyRelativeWorldPoint(currentLandmarks[wristIndex], currentLandmarks);
        Vector3 elbowWorld = GetBodyRelativeWorldPoint(currentLandmarks[elbowIndex], currentLandmarks);

        float maxReach = avatarLeft ? leftArmLength : rightArmLength;
        wristWorld = ClampToReach(shoulderWorld, wristWorld, maxReach * armReachMultiplier);

        if (clampHandVisualZ)
            wristWorld = ClampVisualZ(wristWorld, minHandVisualZ, maxHandVisualZ);

        Vector3 sideDir = avatarLeft ? -transform.right : transform.right;
        Vector3 forwardDir = GetAvatarVisualForward();

        Vector3 stableElbowHint =
            shoulderWorld
            + sideDir * elbowHintSide
            + forwardDir * elbowHintForward
            - transform.up * elbowHintDown;

        Vector3 elbowHintWorld = Vector3.Lerp(
            stableElbowHint,
            elbowWorld,
            elbowLandmarkBlend
        );

        if (!hasSmoothedTargets)
        {
            leftHandTargetSmoothed = wristWorld;
            rightHandTargetSmoothed = wristWorld;
            leftElbowHintSmoothed = elbowHintWorld;
            rightElbowHintSmoothed = elbowHintWorld;

            leftFootTargetSmoothed = leftFoot ? leftFoot.position : transform.position;
            rightFootTargetSmoothed = rightFoot ? rightFoot.position : transform.position;
            leftKneeHintSmoothed = transform.position;
            rightKneeHintSmoothed = transform.position;

            hasSmoothedTargets = true;
        }

        if (avatarLeft)
        {
            leftHandTargetSmoothed = SmoothPoint(leftHandTargetSmoothed, wristWorld, armTargetSmoothness);
            leftElbowHintSmoothed = SmoothPoint(leftElbowHintSmoothed, elbowHintWorld, armTargetSmoothness);

            animator.SetIKPositionWeight(goal, armIKWeight);
            //animator.SetIKRotationWeight(goal, armIKWeight);

            animator.SetIKPosition(goal, leftHandTargetSmoothed);
            //animator.SetIKRotation(goal, stableHandRotation);

            animator.SetIKHintPositionWeight(hint, elbowHintWeight);
            animator.SetIKHintPosition(hint, leftElbowHintSmoothed);

            debugLeftHandTarget = leftHandTargetSmoothed;
            debugLeftElbowHint = leftElbowHintSmoothed;
        }
        else
        {
            rightHandTargetSmoothed = SmoothPoint(rightHandTargetSmoothed, wristWorld, armTargetSmoothness);
            rightElbowHintSmoothed = SmoothPoint(rightElbowHintSmoothed, elbowHintWorld, armTargetSmoothness);

            animator.SetIKPositionWeight(goal, armIKWeight);
            //animator.SetIKRotationWeight(goal, armIKWeight);

            animator.SetIKPosition(goal, rightHandTargetSmoothed);
            //animator.SetIKRotation(goal, stableHandRotation);

            animator.SetIKHintPositionWeight(hint, elbowHintWeight);
            animator.SetIKHintPosition(hint, rightElbowHintSmoothed);

            debugRightHandTarget = rightHandTargetSmoothed;
            debugRightElbowHint = rightElbowHintSmoothed;
        }
    }

    private void UpdateLegIK(bool avatarLeft)
    {
        int hipIndex = GetHipIndex(avatarLeft);
        int kneeIndex = GetKneeIndex(avatarLeft);
        int ankleIndex = GetAnkleIndex(avatarLeft);
        int footIndex = GetFootIndex(avatarLeft);

        Transform upperLeg = avatarLeft ? leftUpperLeg : rightUpperLeg;
        Transform lowerLeg = avatarLeft ? leftLowerLeg : rightLowerLeg;
        Transform foot = avatarLeft ? leftFoot : rightFoot;

        if (upperLeg == null || lowerLeg == null || foot == null)
            return;

        AvatarIKGoal goal = avatarLeft ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot;
        AvatarIKHint hint = avatarLeft ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee;

        Vector3 hipWorld = upperLeg.position;

        int footTargetIndex = useFootIndexForFootTarget ? footIndex : ankleIndex;
        Vector3 footWorld = GetBodyRelativeWorldPoint(currentLandmarks[footTargetIndex], currentLandmarks);
        Vector3 kneeWorld = GetBodyRelativeWorldPoint(currentLandmarks[kneeIndex], currentLandmarks);

        footWorld += transform.up * footTargetYOffset;

        float maxReach = avatarLeft ? leftLegLength : rightLegLength;
        footWorld = ClampToReach(hipWorld, footWorld, maxReach * legReachMultiplier);

        Vector3 sideDir = avatarLeft ? -transform.right : transform.right;
        Vector3 forwardDir = GetAvatarVisualForward();

        Vector3 fixedKneeHint =
            hipWorld
            + sideDir * kneeHintSide
            + forwardDir * kneeHintForward
            - transform.up * kneeHintDown;

        Vector3 hipToFoot = (footWorld - hipWorld).normalized;

        // 地面投影方向（忽略Y轴，避免抬腿高度影响侧方向）
        Vector3 hipToFootFlat = new Vector3(hipToFoot.x, 0f, hipToFoot.z).normalized;

        // 如果腿完全垂直，投影长度为0，fallback到身体侧方
        if (hipToFootFlat.magnitude < 0.001f)
            hipToFootFlat = avatarLeft ? -transform.right : transform.right;

        // 将投影方向旋转90度得到侧方（垂直于腿平面）
        Vector3 legOutward = Vector3.Cross(Vector3.up, hipToFootFlat).normalized;

        // 保证左腿向外是左侧，右腿向外是右侧，否则反向
        Vector3 bodySideDir = avatarLeft ? -transform.right : transform.right;
        if (Vector3.Dot(legOutward, bodySideDir) < 0f)
            legOutward = -legOutward;

        float dynamicForwardAmount = 0.3f;
        float dynamicSideAmount = 0.4f;
        Vector3 dynamicKneeHint = hipWorld
            + hipToFoot * dynamicForwardAmount
            + legOutward * dynamicSideAmount
            - transform.up * kneeHintDown;

        Vector3 stableKneeHint = Vector3.Lerp(
            fixedKneeHint,
            dynamicKneeHint,
            kneeHintDynamicWeight
        );

        Vector3 kneeHintWorld = Vector3.Lerp(
            stableKneeHint,
            kneeWorld,
            kneeLandmarkBlend
        );

        if (avatarLeft)
        {
            leftFootTargetSmoothed = SmoothPoint(leftFootTargetSmoothed, footWorld, legTargetSmoothness);
            leftKneeHintSmoothed = SmoothPoint(leftKneeHintSmoothed, kneeHintWorld, legTargetSmoothness);

            animator.SetIKPositionWeight(goal, legIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, leftFootTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, kneeHintWeight);
            animator.SetIKHintPosition(hint, leftKneeHintSmoothed);

            debugLeftFootTarget = leftFootTargetSmoothed;
            debugLeftKneeHint = leftKneeHintSmoothed;
        }
        else
        {
            rightFootTargetSmoothed = SmoothPoint(rightFootTargetSmoothed, footWorld, legTargetSmoothness);
            rightKneeHintSmoothed = SmoothPoint(rightKneeHintSmoothed, kneeHintWorld, legTargetSmoothness);

            animator.SetIKPositionWeight(goal, legIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, rightFootTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, kneeHintWeight);
            animator.SetIKHintPosition(hint, rightKneeHintSmoothed);

            debugRightFootTarget = rightFootTargetSmoothed;
            debugRightKneeHint = rightKneeHintSmoothed;
        }
    }

    private Vector3 GetBodyRelativeWorldPoint(
        MPNormalizedLandmark landmark,
        List<MPNormalizedLandmark> landmarks
    )
    {
        Vector3 p = GetNormalizedPoint(landmark);
        Vector3 hipCenter = GetHipCenter01(landmarks);

        float dx = p.x - hipCenter.x;
        if (invertTrackingX)
            dx = -dx;

        float localX = dx * trackingWidth;
        float localY = (p.y - hipCenter.y) * trackingHeight + trackingYOffset;

        float visualZ = trackingVisualZOffset;

        if (useLandmarkDepth)
        {
            visualZ += (p.z - hipCenter.z) * landmarkDepthScale * landmarkDepthSign;
        }

        Vector3 localOffset = new Vector3(
            localX,
            localY,
            ConvertVisualZToLocalZ(visualZ)
        );

        return hipsBone.position + transform.TransformDirection(localOffset);
    }

    private Vector3 GetNormalizedPoint(MPNormalizedLandmark lm)
    {
        float x = mirrorInputX ? 1f - lm.x : lm.x;
        float y = flipY ? 1f - lm.y : lm.y;

        return new Vector3(x, y, lm.z);
    }

    private Vector3 GetHipCenter01(List<MPNormalizedLandmark> landmarks)
    {
        Vector3 leftHip = GetNormalizedPoint(landmarks[23]);
        Vector3 rightHip = GetNormalizedPoint(landmarks[24]);
        return (leftHip + rightHip) * 0.5f;
    }

    private float ConvertVisualZToLocalZ(float visualZ)
    {
        //return invertForwardForIK ? -visualZ : visualZ;
        return visualZ;
    }

    private Vector3 GetAvatarVisualForward()
    {
        //return invertForwardForIK ? -transform.forward : transform.forward;
        return transform.forward;
    }

    private Vector3 ClampVisualZ(Vector3 worldPoint, float minVisualZ, float maxVisualZ)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);

        //float visualZ = invertForwardForIK ? -local.z : local.z;
        float visualZ = local.z;
        visualZ = Mathf.Clamp(visualZ, minVisualZ, maxVisualZ);

        local.z = ConvertVisualZToLocalZ(visualZ);

        return transform.TransformPoint(local);
    }

    private Vector3 ClampToReach(Vector3 root, Vector3 target, float maxDistance)
    {
        Vector3 offset = target - root;
        float distance = offset.magnitude;

        if (distance <= maxDistance || distance < 0.0001f)
            return target;

        return root + offset.normalized * maxDistance;
    }

    private Vector3 SmoothPoint(Vector3 current, Vector3 target, float smoothness)
    {
        return Vector3.Lerp(
            current,
            target,
            1f - Mathf.Exp(-smoothness * Time.deltaTime)
        );
    }

    private int GetShoulderIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 11 : 12;
    }

    private int GetElbowIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 13 : 14;
    }

    private int GetWristIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 15 : 16;
    }

    private int GetHipIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 23 : 24;
    }

    private int GetKneeIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 25 : 26;
    }

    private int GetAnkleIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 27 : 28;
    }

    private int GetFootIndex(bool avatarLeft)
    {
        bool useLeftLandmark = swapLeftRightLandmarks ? !avatarLeft : avatarLeft;
        return useLeftLandmark ? 31 : 32;
    }

    private void CacheBones()
    {
        hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);

        leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        leftLowerArm = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);

        rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        rightLowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);

        leftUpperLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftLowerLeg = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);

        rightUpperLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightLowerLeg = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    private void CacheLimbLengths()
    {
        leftArmLength = GetChainLength(leftUpperArm, leftLowerArm, leftHand);
        rightArmLength = GetChainLength(rightUpperArm, rightLowerArm, rightHand);

        leftLegLength = GetChainLength(leftUpperLeg, leftLowerLeg, leftFoot);
        rightLegLength = GetChainLength(rightUpperLeg, rightLowerLeg, rightFoot);
    }

    private float GetChainLength(Transform root, Transform mid, Transform tip)
    {
        if (root == null || mid == null || tip == null)
            return 1f;

        return Vector3.Distance(root.position, mid.position)
             + Vector3.Distance(mid.position, tip.position);
    }

    private void ClearIKWeights()
    {
        ClearArmIKWeights();
        ClearLegIKWeights();
    }

    private void ClearArmIKWeights()
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, 0f);

        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, 0f);
    }

    private void ClearLegIKWeights()
    {
        animator.SetIKPositionWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftFoot, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, 0f);

        animator.SetIKPositionWeight(AvatarIKGoal.RightFoot, 0f);
        animator.SetIKRotationWeight(AvatarIKGoal.RightFoot, 0f);
        animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, 0f);
    }

    void OnDrawGizmos()
    {
        if (!drawDebugTargets || !Application.isPlaying)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(debugLeftHandTarget, 0.05f);
        Gizmos.DrawSphere(debugRightHandTarget, 0.05f);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(debugLeftElbowHint, 0.04f);
        Gizmos.DrawSphere(debugRightElbowHint, 0.04f);

        Gizmos.color = Color.blue;
        Gizmos.DrawSphere(debugLeftFootTarget, 0.05f);
        Gizmos.DrawSphere(debugRightFootTarget, 0.05f);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(debugLeftKneeHint, 0.04f);
        Gizmos.DrawSphere(debugRightKneeHint, 0.04f);
    }
}