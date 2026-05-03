using UnityEngine;
using System.Collections.Generic;
using MPNormalizedLandmark = Mediapipe.Tasks.Components.Containers.NormalizedLandmark;
using Unity.VisualScripting;
using Mediapipe;
using Mediapipe.Tasks.Components.Containers;
using UnityEngine.UIElements;
using Color = UnityEngine.Color;
using Rect = UnityEngine.Rect;

public class MotionRecorder : MonoBehaviour
{
    [Header("Reference")]
    public NewBodyTracker bodyTracker; // 需要在 Inspector 中关联 BodyTracker 组件

    [Header("Recording Settings")]
    [Tooltip("Landmarks to be recorded.")]
    public List<int> trackedIndices = new List<int>
    {
        15, 16, // wrists
        11, 12, // shoulders
        23, 24, // hips
        7, 8,   // head
        13, 14 // elbows
    };

    [Header("Smoothing")]
    public float velocitySmoothness = 10f;
    public float dataHistoryTime = 2f;

    [Header("Output (Read Only)")]
    public float averageSpeed;
    public float maxSpeed;
    public float totalMovement;
    public float motionIntensity;

    private Dictionary<int, Vector3> lastPositions = new Dictionary<int, Vector3>();
    private Dictionary<int, Vector3> smoothedVelocities = new Dictionary<int, Vector3>();

    private Queue<MovementSample> dataHistory = new Queue<MovementSample>();

    [System.Serializable]
    public struct MovementSample
    {
        public float time;
        public float totalSpeed;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (bodyTracker == null)
            bodyTracker = FindObjectOfType<NewBodyTracker>();
    }

    // Update is called once per frame
    void Update()
    {
        if (bodyTracker == null || bodyTracker.currentLandmarksExternal == null) return;

        var landmarks = bodyTracker.currentLandmarksExternal;
        float currentTotalSpeed = 0f;
        float validPointCount = 0;

        foreach (int index in trackedIndices)
        {
            if (index < 0 || index >= landmarks.Count) continue;

            Vector3 currentPos = bodyTracker.GetWorldPointFromLandmark(landmarks[index]);

            if (lastPositions.ContainsKey(index))
            {
                Vector3 rawVelocity = (currentPos - lastPositions[index]) / Time.deltaTime;

                // 平滑速度
                if (!smoothedVelocities.ContainsKey(index))
                    smoothedVelocities[index] = rawVelocity;
                else
                    smoothedVelocities[index] = Vector3.Lerp(smoothedVelocities[index], rawVelocity, velocitySmoothness * Time.deltaTime);

                currentTotalSpeed += smoothedVelocities[index].magnitude;
                validPointCount++;
            }

            lastPositions[index] = currentPos;
        }

        if (validPointCount > 0)
        {
            averageSpeed = currentTotalSpeed / validPointCount;
            if (averageSpeed > maxSpeed) maxSpeed = averageSpeed;

            // 记录历史样本
            dataHistory.Enqueue(new MovementSample { time = Time.time, totalSpeed = averageSpeed });
            // 删除过期样本
            while (dataHistory.Count > 0 && Time.time - dataHistory.Peek().time > dataHistoryTime)
                dataHistory.Dequeue();

            // 计算运动强度（基于近期速度波动）
            float avgHistSpeed = 0f;
            foreach (var s in dataHistory) avgHistSpeed += s.totalSpeed;
            avgHistSpeed /= dataHistory.Count;

            // 强度 = 当前速度 / 历史平均速度（归一化），夹紧到0~1
            if (avgHistSpeed > 0.001f)
                motionIntensity = Mathf.Clamp01(averageSpeed / (avgHistSpeed * 2f)); // 可调系数
            else
                motionIntensity = 0f;
        }
    }

    public void ResetRecording()
    {
        averageSpeed = 0f;
        maxSpeed = 0f;
        totalMovement = 0f;
        motionIntensity = 0f;
        lastPositions.Clear();
        smoothedVelocities.Clear();
        dataHistory.Clear();
    }

    void OnGUI()
    {
        GUIStyle style = new GUIStyle();
        style.fontSize = 24;
        style.normal.textColor = Color.white;
        style.padding = new RectOffset(10, 10, 10, 10);

        GUILayout.BeginArea(new Rect(10, 10, 400, 300), GUI.skin.box);
        GUILayout.Label($"Average Speed: {averageSpeed:F2} m/s", style);
        GUILayout.Label($"Max Speed: {maxSpeed:F2} m/s", style);
        GUILayout.Label($"Total Movement: {totalMovement:F2} m", style);
        GUILayout.Label($"Motion Intensity: {motionIntensity:F2}", style);
        GUILayout.EndArea();
    }
}
