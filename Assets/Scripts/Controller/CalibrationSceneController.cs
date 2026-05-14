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

    public CalibrationAffectiveReadinessData AffectiveReadinessData { get; private set; }

    void Start()
    {
        if (affectiveReadinessPanelController == null && AffectiveReadinessPanel != null)
            affectiveReadinessPanelController = AffectiveReadinessPanel.GetComponent<AffectiveReainessPanel>();

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
        }

        ShowTrackingCheckPanel();
    }

    public void ContinueFromTrackingCheck()
    {
        ShowMotorCognitivePanel();
    }

    public void ContinueFromMotorCognitive()
    {
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("CalibrationSceneController: Game scene name is empty.");
            return;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    private void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
            panel.SetActive(active);
    }
}

[System.Serializable]
public struct CalibrationAffectiveReadinessData
{
    public int mood;
    public int interest;
    public int tension;
    public int fatigue;
}
