using UnityEngine;
using System.Collections.Generic;
using Mediapipe.Tasks.Components.Containers;

public class DanceScoreManager1 : MonoBehaviour
{
    [Header("References")]
    public NewBodyTracker playerTracker;
    public DancePlayback referencePlayback;
    public DanceClipData clip;

    [Header("Scoring Settings")]
    public int[] scoredIndices;
    public float maxErrorThreshold = 0.15f;
    public float smoothFactor = 0.1f;

    [Header("Output (Read Only)")]
    [SerializeField] private float currentAccuracy;
    [SerializeField] private float totalScore;

    private float rawAccuracy;
    private List<int> activeIndices = new List<int>();

    public float Accuracy => currentAccuracy;
    public float Score => totalScore;
    public bool IsPlaying { get; private set; }
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (playerTracker == null) playerTracker = FindAnyObjectByType<NewBodyTracker>();
        if (referencePlayback == null) referencePlayback = FindAnyObjectByType<DancePlayback>();
        if (clip == null && referencePlayback != null) clip = referencePlayback.clip;

        // 确定要评分的索引
        if (scoredIndices == null || scoredIndices.Length == 0)
        {
            if (clip != null) activeIndices = new List<int>(clip.recordedIndices);
            else activeIndices = new List<int>();
        }
        else
        {
            activeIndices = new List<int>(scoredIndices);
        }
    }

    public void StartScoring()
    {
        if (referencePlayback != null)
        {
            referencePlayback.Play();
        }
        IsPlaying = true;
        totalScore = 0f;
        rawAccuracy = 0f;
        currentAccuracy = 0f;
    }

    public void StopScoring()
    {
        IsPlaying = false;
        if (referencePlayback != null) referencePlayback.Stop();
    }

    public void SetSpeed(float speed)
    {
        if (referencePlayback != null) referencePlayback.speedMultiplier = speed;
    }

    void Update()
    {
        if (!IsPlaying) return;
        if (playerTracker == null || playerTracker.currentLandmarksExternal == null) return;
        if (referencePlayback == null || clip == null) return;

        // 获取当前参考帧
        FrameData refFrame = GetFrameAtTime(referencePlayback.PlaybackTime);
        if (refFrame == null) return;

        // 玩家目前的 landmarks
        List<NormalizedLandmark> playerLms = playerTracker.currentLandmarksExternal;

        float totalError = 0f;
        int count = 0;

        foreach (int idx in activeIndices)
        {
            // 需要在 refFrame 中找到该索引对应的坐标
            if (idx >= 33) continue;
            Vector3 refPos = GetReferencePositionForIndex(refFrame, idx);
            Vector3 playerPos = new Vector3(playerLms[idx].x, playerLms[idx].y, playerLms[idx].z);

            totalError += Vector3.Distance(refPos, playerPos);
            count++;
        }

        if (count == 0) return;

        float avgError = totalError / count;
        rawAccuracy = Mathf.Clamp01(1f - (avgError / maxErrorThreshold));
        currentAccuracy = Mathf.Lerp(currentAccuracy, rawAccuracy, 1f - Mathf.Exp(-smoothFactor * Time.deltaTime * 30f));

        // 简易得分累计：每帧根据准确度加分
        totalScore += currentAccuracy * Time.deltaTime * 100f;
    }

    Vector3 GetReferencePositionForIndex(FrameData frame, int landmarkIndex)
    {
        // 在 clip.recordedIndices 中查找 landmarkIndex 的位置
        for (int i = 0; i < clip.recordedIndices.Length; i++)
        {
            if (clip.recordedIndices[i] == landmarkIndex)
            {
                return frame.landmarkPositions[i];
            }
        }
        return Vector3.zero; // 找不到则视为 0，但一般不会发生
    }

    FrameData GetFrameAtTime(float time)
    {
        if (clip.frames == null || clip.frames.Count == 0) return null;
        FrameData best = clip.frames[0];
        for (int i = 1; i < clip.frames.Count; i++)
        {
            if (clip.frames[i].time > time) break;
            best = clip.frames[i];
        }
        return best;
    }
}
