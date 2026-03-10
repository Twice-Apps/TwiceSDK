using UnityEngine;
using UnityEngine.UI;
using System;
using DG.Tweening;

public class TimedButton : MonoBehaviour
{
    public TimeType timeType;
    public int waitTime;
    
    public string uniqueButtonName;
    private string timedButtonID => uniqueButtonName + "UsedCount";
    public Button rewardButton;
    public Text timerText;
    private DateTime lastClickedTime;
    private TimeSpan countdownTime;
    private bool isButtonInteractable;

    public GameObject lockObject;

    private void Awake()
    {
        waitTime = 0; //PlayfabManager.Instance.currentGameSettings.fortuneSpinHoursPerSpin;
        
        if (timeType == TimeType.Hours)
        {
            countdownTime = TimeSpan.FromHours(waitTime);
        }
        else
        {
            countdownTime = TimeSpan.FromMinutes(waitTime);
        }
    }

   void Start()
    {
        //rewardButton.onClick.AddListener(OnSpinFinished);
        CheckButtonStatus();
    }

    void Update()
    {
        if (!isButtonInteractable) 
        {
            UpdateTimer();
        }
    }

    public void OnSpinFinished()
    {
        lastClickedTime = DateTime.Now;
        PlayerPrefs.SetString(uniqueButtonName, lastClickedTime.ToString());
        PlayerPrefs.Save();
        
        CheckButtonStatus();
    }

    void CheckButtonStatus()
    {
        if (PlayerPrefs.HasKey(uniqueButtonName))
        {
            lastClickedTime = DateTime.Parse(PlayerPrefs.GetString(uniqueButtonName));
            TimeSpan timeSinceLastClick = DateTime.Now - lastClickedTime;
            
            if (timeSinceLastClick < countdownTime)
            {
                rewardButton.interactable = false;
                isButtonInteractable = false;
                lockObject.SetActive(true);
                UpdateTimer(); // Geri sayımı başlat
                return;
            }
            
            PlayerPrefs.DeleteKey(uniqueButtonName);
        }
        
        rewardButton.interactable = true;
        isButtonInteractable = true;
        timerText.text = "";
        lockObject.SetActive(false);
    }

    void UpdateTimer()
    {
        TimeSpan timeSinceLastClick = DateTime.Now - lastClickedTime;
        TimeSpan remainingTime = countdownTime - timeSinceLastClick;

        if (remainingTime <= TimeSpan.Zero)
        {
            rewardButton.interactable = true;
            isButtonInteractable = true;
            timerText.text = "";
            lockObject.SetActive(false);
        }
        else
        {
            rewardButton.interactable = false;
            timerText.text = string.Format("{0:D2}:{1:D2}:{2:D2}",
                                           remainingTime.Hours,
                                           remainingTime.Minutes,
                                           remainingTime.Seconds);
        }
    }
}
public enum TimeType {Hours, Minutes}