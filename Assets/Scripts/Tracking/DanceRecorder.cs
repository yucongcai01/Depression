using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using Unity.VisualScripting;
using MPNormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;

public class DanceRecorder : MonoBehaviour
{
    [Header("Input")]
    public NewBodyTracker bodyTracker; // 需要在 Inspector 中关联 BodyTracker 组件
    public Animator animator; // 需要在 Inspector 中关联 Animator 组件

    [Header("Recording Settings")]
    [Tooltip("Landmarks collected from bodytracker")]
    public bool autoCollectIndices = true;
    public int[] manualRecordIndices; // 手动指定需要录制的landmark索引
    private List<int> recordIndices = new List<int>();
    private List<FrameData> recordedFrames = new List<FrameData>();
    private bool isRecording = false;
    private float recordingStartTime;
    public bool IsRecording => isRecording;
    public float CurrentRecordingTime => isRecording ? Time.time - recordingStartTime : 0f;

    public int clipCount = 0; // 已保存的片段数量，用于生成默认文件名

    private float recordedDuration = 0f;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (bodyTracker == null)
        {
            bodyTracker = FindAnyObjectByType<NewBodyTracker>();
            if (bodyTracker == null)
            {
                Debug.LogError("DanceRecorder: No NewBodyTracker found.");
                return;
            }
        }

        // 检查已保存的片段数量，避免覆盖
#if UNITY_EDITOR
        string[] existingClips = UnityEditor.AssetDatabase.FindAssets("t:DanceClipData", new[] { "Assets/Resources/DanceClips" });
        clipCount = existingClips.Length;
#endif
    }

    // Update is called once per frame
    void Update()
    {
        if (!isRecording) return;

        // 取得当前帧的 landmarks 快照
        List<MPNormalizedLandmark> landmarks = bodyTracker.currentLandmarksExternal;
        if (landmarks == null)
        {
            Debug.LogWarning("DanceRecorder: No landmarks detected in current frame.");
            return;
        }

        foreach (int idx in recordIndices)
        {
            if (idx < 0 || idx >= landmarks.Count)
            {
                Debug.LogWarning($"DanceRecorder: Landmark index {idx} is out of bounds.");
                continue;
            }
        }


        FrameData frame = new FrameData();
        frame.time = Time.time - recordingStartTime;
        frame.landmarkPositions = new Vector3[recordIndices.Count];

        for (int i = 0; i < recordIndices.Count; i++)
        {
            int idx = recordIndices[i];
            if (idx < 0 || idx >= landmarks.Count)
            {
                frame.landmarkPositions[i] = Vector3.zero;
                continue;
            }

            var lm = landmarks[idx];
            // Store the viewport/depth values that match NewBodyTracker's bone-driving space.
            frame.landmarkPositions[i] = GetRecordedLandmarkPosition(lm);
        }

        recordedFrames.Add(frame);
    }

    private Vector3 GetRecordedLandmarkPosition(MPNormalizedLandmark lm)
    {
        // Keep recorded clips in the same viewport/depth space NewBodyTracker uses to drive bones.
        float viewportX = bodyTracker.mirror ? 1f - lm.x : lm.x;
        float viewportY = bodyTracker.flipY ? 1f - lm.y : lm.y;
        float depth = lm.z * bodyTracker.depthMultiplier + bodyTracker.depthOffset;
        return new Vector3(viewportX, viewportY, depth);
    }

    public void CollectTrackedIndices()
    {
        if (!autoCollectIndices)
        {
            recordIndices = manualRecordIndices.ToList();
            return;
        }

        HashSet<int> indices = new HashSet<int>();

        foreach (var mapping in bodyTracker.rotationMappings)
        {
            if (!mapping.useParentMidpoint)
                indices.Add(mapping.parentLandmarkIndex);
            else
            {
                indices.Add(mapping.parentMidpointA);
                indices.Add(mapping.parentMidpointB);
            }

            if (!mapping.useChildMidpoint)
                indices.Add(mapping.childLandmarkIndex);
            else
            {
                indices.Add(mapping.childMidpointA);
                indices.Add(mapping.childMidpointB);
            }
        }

        recordIndices = indices.OrderBy(i => i).ToList();
        manualRecordIndices = recordIndices.ToArray(); // 同步到 Inspector
        Debug.Log($"DanceRecorder: Collected {recordIndices.Count} unique landmark indices for recording.");
    }

    public void StartRecording()
    {
        if (isRecording) return;
        if (recordIndices.Count == 0)
        {
            CollectTrackedIndices();
        }

        if (recordIndices.Count == 0)
        {
            Debug.LogError("DanceRecorder: No landmark indices to record. Please check your rotation mappings or specify manual indices.");
            return;
        }

        recordedFrames.Clear();
        isRecording = true;
        recordingStartTime = Time.time;
        recordedDuration = 0f;
        Debug.Log("DanceRecorder: Recording started.");
    }

    public int StopRecording()
    {
        if (!isRecording) return 0;
        recordedDuration = Time.time - recordingStartTime;
        isRecording = false;
        Debug.Log($"DanceRecorder: Recording stopped, total frames: {recordedFrames.Count}, duration: {recordedDuration:F2}s.");
        return recordedFrames.Count;
    }

    // 将clipCount作为参数传入，生成不同的文件名
    public DanceClipData SaveToAsset()
    {
        // 确保目标文件夹存在
    #if UNITY_EDITOR
        string folder = "Assets/Resources/DanceClips";
        if (!System.IO.Directory.Exists(folder))
            System.IO.Directory.CreateDirectory(folder);
    #endif

        // 生成唯一文件名（基于当前 clipCount）
        string path = $"Assets/Resources/DanceClips/DanceClip_{clipCount:D3}.asset";

        DanceClipData clip = ScriptableObject.CreateInstance<DanceClipData>();
        clip.Duration = recordedDuration;
        clip.recordedIndices = recordIndices.ToArray();
        clip.frames = new List<FrameData>(recordedFrames);

    #if UNITY_EDITOR
        UnityEditor.AssetDatabase.CreateAsset(clip, path);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"DanceRecorder: Dance clip saved to {path}");
    #endif

        Debug.Log($"Total recorded frames: {recordedFrames.Count}, duration: {recordedDuration:F2}s, saved as {path}");

        // 保存成功后递增 clipCount，为下次保存准备新编号
        clipCount++;

        return clip;
    }
}
