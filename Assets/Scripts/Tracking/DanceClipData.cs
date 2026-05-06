using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Playables;

[CreateAssetMenu(fileName = "NewDanceClip", menuName = "Dance Clip")]

public class DanceClipData : ScriptableObject
{
    public float Duration;
    public int[] recordedIndices; // 片段录制的landmark索引
    public List<FrameData> frames = new List<FrameData>();
}

/// <summary>
/// 单帧数据 = 时间 + 对应landmark索引的坐标列表
/// </summary>
[System.Serializable]
public class FrameData
{
    public float time; // 当前帧时间点
    public Vector3[] landmarkPositions; // 对应recordIndices的坐标列表
}
