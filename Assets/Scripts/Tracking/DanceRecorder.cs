using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Mediapipe.Unity.Sample.PoseLandmarkDetection;
using Unity.VisualScripting;

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
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (bodyTracker == null)
        {
            bodyTracker = FindObjectOfType<NewBodyTracker>();
            if (bodyTracker == null)
            {
                Debug.LogError("DanceRecorder: No NewBodyTracker found.");
                return;
            }
        }

        CollectTrackedIndices();
    }

    // Update is called once per frame
    void Update()
    {
        if (!isRecording) return;

        // 取得当前帧的 landmarks 快照
        List<Mediapipe.Tasks.Components.Containers.NormalizedLandmark> landmarks = bodyTracker.currentLandmarksExternal;
        if (landmarks == null || landmarks.Count < 33)
            return;

        FrameData frame = new FrameData();
        frame.time = Time.time - recordingStartTime;
        frame.landmarkPositions = new Vector3[recordIndices.Count];

        for (int i = 0; i < recordIndices.Count; i++)
        {
            int idx = recordIndices[i];
            var lm = landmarks[idx];
            // 存储原始归一化坐标（未做镜像处理，由回放时的 GetWorldPointFromLandmark 统一应用）
            frame.landmarkPositions[i] = new Vector3(lm.x, lm.y, lm.z);
        }

        recordedFrames.Add(frame);
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
    }

    public void StartRecording()
    {
        if (isRecording) return;
        if (recordIndices.Count == 0)
        {
            Debug.LogWarning("DanceRecorder: no tracked landmark indices found.");
            return;
        }

        recordedFrames.Clear();
        isRecording = true;
        recordingStartTime = Time.time;
        Debug.Log("DanceRecorder: Recording started.");
    }

    public int StopRecording()
    {
        if (!isRecording) return 0;
        isRecording = false;
        Debug.Log($"DanceRecorder: Recording stopped, total frames: {recordedFrames.Count}");
        return recordedFrames.Count;
    }

    public DanceClipData SaveToAsset(string path = "Assets/DanceClip.asset")
    {
        DanceClipData clip = ScriptableObject.CreateInstance<DanceClipData>();
        clip.Duration = CurrentRecordingTime;
        clip.recordedIndices = recordIndices.ToArray();
        clip.frames = new List<FrameData>(recordedFrames);

#if UNITY_EDITOR
        UnityEditor.AssetDatabase.CreateAsset(clip, path);
        UnityEditor.AssetDatabase.SaveAssets();
        Debug.Log($"DanceRecorder: Dance clip saved to {path}");
#endif
        return clip;
    }
}
