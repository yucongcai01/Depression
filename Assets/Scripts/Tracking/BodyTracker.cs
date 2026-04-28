using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using Mediapipe.Tasks.Vision.PoseLandmarker;

public class BodyTracker : MonoBehaviour
{
    [Header("References")]
    public PoseLandmarkerRunner poseLandmarkerRunner;
    public Animator animator;

    [Header("Settings")]
    public Vector3 bodyOffset = new Vector3(0, 0.8f, 2f);
    public float bodyScale = 1.5f;

    [System.Serializable]
    public struct JointMapping
    {
        public HumanBodyBones humanBone;
        public int mediaPipeIndex;
    }

    public List<JointMapping> jointMappings = new List<JointMapping>();
    private Dictionary<HumanBodyBones, Transform> boneIndexMap = new Dictionary<HumanBodyBones, Transform>();


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        InitializeBoneIndexMap();

        if (animator != null)
        {
            foreach (var mapping in jointMappings)
            {
                Transform bone = animator.GetBoneTransform(mapping.humanBone);
                if (bone != null && !boneIndexMap.ContainsKey(mapping.humanBone))
                {
                    boneIndexMap.Add(mapping.humanBone, bone);
                }
                else
                {
                    Debug.LogError($"Bone {mapping.humanBone} not found on the Animator.");
                }
            }
        }
        else
        {
            Debug.LogError("Animator component not found on the GameObject.");
        }

    }

    // Update is called once per frame
    void Update()
    {
        if (poseLandmarkerRunner != null)
        {
            UpdateBodyPose();
        }
        else
        {
            Debug.LogError("PoseLandmarkerRunner component not found on the GameObject.");
        }
    }

    private void InitializeBoneIndexMap()
    {
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.Head, mediaPipeIndex = 0 });

        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftUpperArm, mediaPipeIndex = 11 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightUpperArm, mediaPipeIndex = 12 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftLowerArm, mediaPipeIndex = 13 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightLowerArm, mediaPipeIndex = 14 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftHand, mediaPipeIndex = 15 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightHand, mediaPipeIndex = 16 });
        
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftUpperLeg, mediaPipeIndex = 23 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightUpperLeg, mediaPipeIndex = 24 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftLowerLeg, mediaPipeIndex = 25 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightLowerLeg, mediaPipeIndex = 26 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.LeftFoot, mediaPipeIndex = 27 });
        jointMappings.Add(new JointMapping { humanBone = HumanBodyBones.RightFoot, mediaPipeIndex = 28 });
    }

    private void UpdateBodyPose()
    {
        var result = poseLandmarkerRunner.latestResult;
        if (result.poseLandmarks == null || result.poseLandmarks.Count == 0)
        {
            return;
        }
        
        var landmarks = result.poseLandmarks[0].landmarks;
        if (landmarks == null || landmarks.Count < 33)
        {
            return;
        }


        foreach (var mapping in jointMappings)
        {
            if (boneIndexMap.TryGetValue(mapping.humanBone, out Transform bone))
            {
                if (mapping.mediaPipeIndex < landmarks.Count)
                {
                    var lm = landmarks[mapping.mediaPipeIndex];
                    /*float x = lm.x;
                    float y = lm.y;
                    float z = lm.z;
                    float mirroredX = 1f - x;

                    Vector3 relative = new Vector3(mirroredX, y, z);
                    Vector3 targetPosition = relative * bodyScale + bodyOffset;

                    bone.position = targetPosition;*/
                    float viewportX = 1f - lm.x;
                    float viewportY = 1f - lm.y;
                    float depth = lm.z * -10f +5f;

                    Vector3 viewportPoint = new Vector3(viewportX, viewportY, depth);
                    Vector3 worldPoint = Camera.main.ViewportToWorldPoint(viewportPoint);
                    bone.position = worldPoint;
                }
                else
                {
                    Debug.LogError($"MediaPipe landmark index {mapping.mediaPipeIndex} is out of range.");
                }
            }
        }
    }
}
