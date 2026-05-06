using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI 控制器：管理录制按钮、倒计时、自动停止
/// </summary>
public class DanceUIController : MonoBehaviour
{
    [Header("References")]
    public DanceRecorder danceRecorder;          // 你的录制脚本

    [Header("UI Elements")]
    public Button startButton;                   // “开始录制”按钮
    public Button stopButton;                    // “停止”按钮（可用于中途停止）
    public TMP_Text statusText;                      // 显示倒计时/状态的文本框

    [Header("Recording Settings")]
    public float countdownBeforeRecord = 3f;     // 按下按钮后的倒计时秒数
    public float recordingDuration = 15f;        // 录制时长

    private Coroutine recordCoroutine;
    private bool isCountingDown = false;
    private bool isRecording = false;

    void Start()
    {
        // 初始化按钮监听
        if (startButton != null)
            startButton.onClick.AddListener(OnStartButtonClicked);

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(OnStopButtonClicked);
            stopButton.interactable = false;     // 初始不可用
        }

        UpdateStatusText("Waiting to start recording...");
    }

    /// <summary>
    /// 点击“开始录制”按钮
    /// </summary>
    public void OnStartButtonClicked()
    {
        Debug.Log("RecordUIController: Start button clicked.");
        if (isCountingDown || isRecording) return;

        // 如果有正在运行的协程则停掉
        if (recordCoroutine != null)
            StopCoroutine(recordCoroutine);

        recordCoroutine = StartCoroutine(RecordSequence());
    }

    /// <summary>
    /// 点击“停止”按钮（手动提前终止）
    /// </summary>
    public void OnStopButtonClicked()
    {
        if (!isRecording && !isCountingDown) return;

        // 停止协程
        if (recordCoroutine != null)
        {
            StopCoroutine(recordCoroutine);
            recordCoroutine = null;
        }

        // 如果正在录制，调用停止
        if (danceRecorder != null && danceRecorder.IsRecording)
            danceRecorder.StopRecording();

        isCountingDown = false;
        isRecording = false;
        UpdateUIState(false);
        UpdateStatusText("Recording stopped.");
    }

    /// <summary>
    /// 完整录制流程协程：倒计时 → 录制 → 自动停止
    /// </summary>
    IEnumerator RecordSequence()
    {
        // ---- 1. 倒计时阶段 ----
        isCountingDown = true;
        UpdateUIState(false);                     // 禁用按钮，不可再次开始

        float timer = countdownBeforeRecord;
        while (timer > 0f)
        {
            UpdateStatusText($"Countdown: {Mathf.CeilToInt(timer)} seconds");
            yield return new WaitForSeconds(1f);
            timer -= 1f;
        }

        // ---- 2. 开始录制 ----
        if (danceRecorder == null)
        {
            Debug.LogError("DanceUIController: DanceRecorder is not assigned.");
            isCountingDown = false;
            UpdateUIState(true);
            yield break;
        }

        danceRecorder.StartRecording();
        isCountingDown = false;
        isRecording = true;
        UpdateUIState(false);                     // 录制期间开始按钮不可用，但停止按钮可用

        float recordTimer = recordingDuration;
        while (recordTimer > 0f)
        {
            UpdateStatusText($"Recording... {Mathf.CeilToInt(recordTimer)} seconds remaining");
            yield return new WaitForSeconds(1f);
            recordTimer -= 1f;
        }

        // ---- 3. 自动停止 ----
        danceRecorder.StopRecording();
        isRecording = false;
        
        #if UNITY_EDITOR
        danceRecorder.SaveToAsset(); // 录制完成后自动保存
        #endif

        UpdateUIState(true);                      // 恢复按钮状态
        UpdateStatusText("Recording completed!");
        Debug.Log("DanceUIController: Recording completed.");

        recordCoroutine = null;
    }

    /// <summary>
    /// 更新按钮的交互状态
    /// </summary>
    void UpdateUIState(bool canStart)
    {
        if (startButton != null)
            startButton.interactable = canStart && !isCountingDown && !isRecording;

        if (stopButton != null)
            stopButton.interactable = (isCountingDown || isRecording);
    }

    /// <summary>
    /// 刷新状态文本
    /// </summary>
    void UpdateStatusText(string msg)
    {
        if (statusText != null)
            statusText.text = msg;
    }
}