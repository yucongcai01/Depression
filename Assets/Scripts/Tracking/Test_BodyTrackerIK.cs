using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using MPNormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

public class BodyTrackerIK : MonoBehaviour
{
    [Header("References")]
    public PoseLandmarkerRunner poseLandmarkerRunner;
    public Animator animator;

    [Header("Mirror Mode")]
    public bool mirror = false;
    public bool flipY = true;

    [Header("Avatar Placement")]
    public bool lockAvatarTransform = true;
    public Vector3 avatarInitialPosition = Vector3.zero;
    public Vector3 avatarInitialRotation = new Vector3(0f, 180f, 0f);

    [Header("Avatar Scale")]
    public float bodyScale = 1f;
    public float scaleSmoothness = 10f;

    [Header("Tracking Space")]
    public float trackingWidth = 2.2f;
    public float trackingHeight = 2.2f;
    public bool invertTrackingX = true;

    public bool useLandmarkDepth = false;
    public float landmarkDepthScale = 0.5f;
    public float landmarkDepthSign = -1f;

    public float trackingYOffset = 0f;
    public float trackingZOffset = 0f;

    [Header("Arm IK")]
    public bool armsEnabled = true;
    [Range(0f, 1f)]
    public float armIKWeight = 1f;
    [Range(0f, 1f)]
    public float elbowHintWeight = 1f;

    public float armTargetSmoothness = 20f;
    public float armReachMultiplier = 0.98f;

    [Header("Elbow Hint")]
    public float elbowHintSide = 0.45f;
    public float elbowHintForward = 0.25f;
    public float elbowHintDown = 0.15f;

    [Range(0f, 1f)]
    public float elbowLandmarkBlend = 0.25f;

    [Header("Hand Local Z Clamp")]
    public bool clampHandLocalZ = true;
    public float minHandLocalZ = -0.35f;
    public float maxHandLocalZ = 1.2f;

    [Header("Leg IK")]
    public bool legsEnabled = false;
    [Range(0f, 1f)]
    public float legIKWeight = 0.5f;
    [Range(0f, 1f)]
    public float kneeHintWeight = 0.7f;

    public float legTargetSmoothness = 15f;
    public float legReachMultiplier = 0.98f;

    [Header("Knee Hint")]
    public float kneeHintForward = 0.5f;
    public float kneeHintSide = 0.1f;
    public float kneeHintDown = 0.5f;

    [Range(0f, 1f)]
    public float kneeLandmarkBlend = 0.15f;

    private List<MPNormalizedLandmark> currentLandmarks;

    private Vector3 initialLocalScale;

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

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();

        if (animator == null)
        {
            Debug.LogError("BodyTrackerIK: No Animator found.");
            enabled = false;
            return;
        }

        animator.applyRootMotion = false;

        if (lockAvatarTransform)
        {
            avatarInitialPosition = transform.position;
            avatarInitialRotation = transform.eulerAngles;
        }

        initialLocalScale = transform.localScale;

        CacheBones();
        CacheLimbLengths();

        if (hipsBone == null)
        {
            Debug.LogError("BodyTrackerIK: Hips bone not found. Please check Humanoid Avatar mapping.");
            enabled = false;
            return;
        }
    }

    void Update()
    {
        if (poseLandmarkerRunner == null)
        {
            Debug.LogError("BodyTrackerIK: PoseLandmarkerRunner not assigned.");
            return;
        }

        var landmarks = GetLandmarksSnapshot();
        if (landmarks == null) return;

        currentLandmarks = landmarks;

        if (lockAvatarTransform)
        {
            transform.position = avatarInitialPosition;
            transform.rotation = Quaternion.Euler(avatarInitialRotation);
        }

        Vector3 targetScale = initialLocalScale * bodyScale;
        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            Time.deltaTime * scaleSmoothness
        );
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

    private void UpdateArmIK(bool left)
    {
        int shoulderIndex = left ? 11 : 12;
        int elbowIndex = left ? 13 : 14;
        int wristIndex = left ? 15 : 16;

        Transform upperArm = left ? leftUpperArm : rightUpperArm;
        Transform lowerArm = left ? leftLowerArm : rightLowerArm;
        Transform hand = left ? leftHand : rightHand;

        if (upperArm == null || lowerArm == null || hand == null)
            return;

        AvatarIKGoal goal = left ? AvatarIKGoal.LeftHand : AvatarIKGoal.RightHand;
        AvatarIKHint hint = left ? AvatarIKHint.LeftElbow : AvatarIKHint.RightElbow;

        Vector3 shoulderWorld = upperArm.position;
        Vector3 wristWorld = GetBodyRelativeWorldPoint(currentLandmarks[wristIndex], currentLandmarks);
        Vector3 elbowWorld = GetBodyRelativeWorldPoint(currentLandmarks[elbowIndex], currentLandmarks);

        float maxReach = left ? leftArmLength : rightArmLength;
        wristWorld = ClampToReach(shoulderWorld, wristWorld, maxReach * armReachMultiplier);

        if (clampHandLocalZ)
            wristWorld = ClampHandDepth(wristWorld);

        Vector3 sideDir = left ? -transform.right : transform.right;

        Vector3 stableElbowHint =
            shoulderWorld
            + sideDir * elbowHintSide
            + transform.forward * elbowHintForward
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
            hasSmoothedTargets = true;
        }

        if (left)
        {
            leftHandTargetSmoothed = SmoothPoint(leftHandTargetSmoothed, wristWorld, armTargetSmoothness);
            leftElbowHintSmoothed = SmoothPoint(leftElbowHintSmoothed, elbowHintWorld, armTargetSmoothness);

            animator.SetIKPositionWeight(goal, armIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, leftHandTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, elbowHintWeight);
            animator.SetIKHintPosition(hint, leftElbowHintSmoothed);
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
        }
    }

    private void UpdateLegIK(bool left)
    {
        int hipIndex = left ? 23 : 24;
        int kneeIndex = left ? 25 : 26;
        int ankleIndex = left ? 27 : 28;

        Transform upperLeg = left ? leftUpperLeg : rightUpperLeg;
        Transform lowerLeg = left ? leftLowerLeg : rightLowerLeg;
        Transform foot = left ? leftFoot : rightFoot;

        if (upperLeg == null || lowerLeg == null || foot == null)
            return;

        AvatarIKGoal goal = left ? AvatarIKGoal.LeftFoot : AvatarIKGoal.RightFoot;
        AvatarIKHint hint = left ? AvatarIKHint.LeftKnee : AvatarIKHint.RightKnee;

        Vector3 hipWorld = upperLeg.position;
        Vector3 ankleWorld = GetBodyRelativeWorldPoint(currentLandmarks[ankleIndex], currentLandmarks);
        Vector3 kneeWorld = GetBodyRelativeWorldPoint(currentLandmarks[kneeIndex], currentLandmarks);

        float maxReach = left ? leftLegLength : rightLegLength;
        ankleWorld = ClampToReach(hipWorld, ankleWorld, maxReach * legReachMultiplier);

        Vector3 sideDir = left ? -transform.right : transform.right;

        Vector3 stableKneeHint =
            hipWorld
            + sideDir * kneeHintSide
            + transform.forward * kneeHintForward
            - transform.up * kneeHintDown;

        Vector3 kneeHintWorld = Vector3.Lerp(
            stableKneeHint,
            kneeWorld,
            kneeLandmarkBlend
        );

        if (left)
        {
            leftFootTargetSmoothed = SmoothPoint(leftFootTargetSmoothed, ankleWorld, legTargetSmoothness);
            leftKneeHintSmoothed = SmoothPoint(leftKneeHintSmoothed, kneeHintWorld, legTargetSmoothness);

            animator.SetIKPositionWeight(goal, legIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, leftFootTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, kneeHintWeight);
            animator.SetIKHintPosition(hint, leftKneeHintSmoothed);
        }
        else
        {
            rightFootTargetSmoothed = SmoothPoint(rightFootTargetSmoothed, ankleWorld, legTargetSmoothness);
            rightKneeHintSmoothed = SmoothPoint(rightKneeHintSmoothed, kneeHintWorld, legTargetSmoothness);

            animator.SetIKPositionWeight(goal, legIKWeight);
            animator.SetIKRotationWeight(goal, 0f);
            animator.SetIKPosition(goal, rightFootTargetSmoothed);

            animator.SetIKHintPositionWeight(hint, kneeHintWeight);
            animator.SetIKHintPosition(hint, rightKneeHintSmoothed);
        }
    }

    private Vector3 GetBodyRelativeWorldPoint(
        MPNormalizedLandmark landmark,
        List<MPNormalizedLandmark> landmarks
    )
    {
        Vector3 p = GetNormalizedPoint(landmark);

        Vector3 leftHip = GetNormalizedPoint(landmarks[23]);
        Vector3 rightHip = GetNormalizedPoint(landmarks[24]);
        Vector3 hipCenter = (leftHip + rightHip) * 0.5f;

        /*
        float localX = (p.x - hipCenter.x) * trackingWidth;
        float localY = (p.y - hipCenter.y) * trackingHeight + trackingYOffset;
        */
        float dx = p.x - hipCenter.x;
        float localX = dx * trackingWidth * (invertTrackingX ? -1f : 1f);
        float localY = (p.y - hipCenter.y) * trackingHeight + trackingYOffset;

        float localZ = trackingZOffset;

        if (useLandmarkDepth)
        {
            localZ += (p.z - hipCenter.z) * landmarkDepthScale * landmarkDepthSign;
        }

        Vector3 localOffset = new Vector3(localX, localY, localZ);

        return hipsBone.position + transform.TransformDirection(localOffset);
    }

    private Vector3 GetNormalizedPoint(MPNormalizedLandmark lm)
    {
        float x = mirror ? 1f - lm.x : lm.x;
        float y = flipY ? 1f - lm.y : lm.y;

        return new Vector3(x, y, lm.z);
    }

    private Vector3 ClampToReach(Vector3 root, Vector3 target, float maxDistance)
    {
        Vector3 offset = target - root;
        float distance = offset.magnitude;

        if (distance <= maxDistance || distance < 0.0001f)
            return target;

        return root + offset.normalized * maxDistance;
    }

    private Vector3 ClampHandDepth(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        local.z = Mathf.Clamp(local.z, minHandLocalZ, maxHandLocalZ);
        return transform.TransformPoint(local);
    }

    private Vector3 SmoothPoint(Vector3 current, Vector3 target, float smoothness)
    {
        return Vector3.Lerp(
            current,
            target,
            1f - Mathf.Exp(-smoothness * Time.deltaTime)
        );
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
}