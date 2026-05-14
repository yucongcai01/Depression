using UnityEngine;
using UnityEngine.SceneManagement;

public class CalibrationSceneController : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject AffectiveReadinessPanel;
    [SerializeField] private GameObject TrackingCheckPanel;
    [SerializeField] private GameObject MotorCognitivePanel;

    [Header("Scene Names")]
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Panel Controllers")]
    [SerializeField] private AffectiveReainessPanel affectiveReadinessPanelController;
    [SerializeField] private CalibrationReadinessRecorder readinessRecorder;

    public CalibrationAffectiveReadinessData AffectiveReadinessData { get; private set; }

    void Start()
    {
        if (affectiveReadinessPanelController == null && AffectiveReadinessPanel != null)
            affectiveReadinessPanelController = AffectiveReadinessPanel.GetComponent<AffectiveReainessPanel>();

        if (readinessRecorder == null)
            readinessRecorder = GetComponent<CalibrationReadinessRecorder>();

        if (readinessRecorder == null)
            readinessRecorder = gameObject.AddComponent<CalibrationReadinessRecorder>();

        if (affectiveReadinessPanelController != null)
            affectiveReadinessPanelController.Initialize(this);

        ShowAffectiveReadinessPanel();
    }

    public void ShowAffectiveReadinessPanel()
    {
        SetPanelActive(AffectiveReadinessPanel, true);
        SetPanelActive(TrackingCheckPanel, false);
        SetPanelActive(MotorCognitivePanel, false);
    }
    
    public void ShowTrackingCheckPanel()
    {
        SetPanelActive(AffectiveReadinessPanel, false);
        SetPanelActive(TrackingCheckPanel, true);
        SetPanelActive(MotorCognitivePanel, false);
    }
    
    public void ShowMotorCognitivePanel()
    {
        SetPanelActive(AffectiveReadinessPanel, false);
        SetPanelActive(TrackingCheckPanel, false);
        SetPanelActive(MotorCognitivePanel, true);
    }

    public void ContinueFromAffectiveReadiness()
    {
        if (affectiveReadinessPanelController != null)
        {
            if (!affectiveReadinessPanelController.CanContinue)
                return;

            AffectiveReadinessData = affectiveReadinessPanelController.GetData();
            if (readinessRecorder != null)
                readinessRecorder.RecordAffectiveReadiness(AffectiveReadinessData);
        }

        ShowTrackingCheckPanel();
    }

    public void ContinueFromTrackingCheck()
    {
        if (readinessRecorder != null)
            readinessRecorder.RecordPhysicalReadiness(trackingConfirmed: true);

        ShowMotorCognitivePanel();
    }

    public void ContinueFromMotorCognitive()
    {
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("CalibrationSceneController: Game scene name is empty.");
            return;
        }

        if (readinessRecorder != null)
        {
            readinessRecorder.RecordMotorCognitiveReadiness();
            readinessRecorder.FinalizeSession();
        }

        SceneManager.LoadScene(gameSceneName);
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }
}
