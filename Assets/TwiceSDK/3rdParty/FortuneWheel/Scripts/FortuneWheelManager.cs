using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;
using System.Linq;
using AssetKits.ParticleImage;
using DG.Tweening;
using UnityEngine.Events;
using TMPro;
using Ease = DG.Tweening.Ease;

#if UNITY_EDITOR
using UnityEditor;
#endif
public class FortuneWheelManager : MonoBehaviour
{
    [Header("Game Objects for some elements")]
    public Button turnButton;
    public TimedButton timedButton;// This button is showed when you can turn the wheel for coins
    public Button btnClose;
    public GameObject Circle;                   // Rotatable GameObject on scene with reward objects
    private bool _isStarted;                    // Flag that the wheel is spinning

    [Header("Params for each sector")]
    public FortuneWheelSector[] Sectors;        // All sectors objects

    private float _finalAngle;                  // The final angle is needed to calculate the reward
    private float _startAngle;                  // The first time start angle equals 0 but the next time it equals the last final angle
    private float _currentLerpRotationTime;     // Needed for spinning animation


    // Flag that player can turn the wheel for free right now
    private bool _isFreeTurnAvailable;

    private FortuneWheelSector _finalSector;
    int timeSpinFree = 10800;

    public GameObject earnedObj;
    public Text earnedValueText;
    public CanvasGroup canvasGroup;

    public ParticleImage particle;
    public Transform timerTransform;
    
    private void Start()
    {
        UpdateValueReward();
    }
    public void ShakeText()
    {
        timerTransform.DOPunchScale(Vector3.one / 10f, .35f, 10, 10).OnComplete(() =>
        {
            timerTransform.localScale = Vector3.one;
        });
    }
    private void OnEnable()
    {
        canvasGroup.alpha = 0;
        earnedObj.SetActive(false);

        canvasGroup.DOFade(1, 0.5f);
    }
    
    public void ClosePopup()
    {
        gameObject.SetActive(false);
    }
    void UpdateValueReward()
    {
        foreach (var sector in Sectors)
        {
            if (sector.ValueTextObject != null)
            {
                double moneyReward = sector.RewardValue;
                if (sector.typeRewardSpin == TypeRewardSpin.Pass)
                {
                    
                }
                else if (sector.typeRewardSpin == TypeRewardSpin.Gold)
                {
                    //moneyReward = PlayerPrefSave.LevelCurrent * 2 * moneyReward - moneyReward;,
                    moneyReward = sector.RewardValue;
                }
                //sector.ValueTextObject.GetComponent<Text>().text = CurrencyManager.Convert(moneyReward).ToString();
            }
        }
    }
    
    public void Spin()
    {
        turnButton.interactable = false;
        TurnWheel();
    }
    private void TurnWheel()
    {
        earnedObj.SetActive(false);
        
        _currentLerpRotationTime = 0f;

        // All sectors angles
        int[] sectorsAngles = new int[Sectors.Length];

        // Fill the necessary angles (for example if we want to have 12 sectors we need to fill the angles with 30 degrees step)
        // It's recommended to use the EVEN sectors count (2, 4, 6, 8, 10, 12, etc)
        for (int i = 1; i <= Sectors.Length; i++)
        {
            sectorsAngles[i - 1] = 360 / Sectors.Length * i;
        }

        //int cumulativeProbability = Sectors.Sum(sector => sector.Probability);

        double rndNumber = UnityEngine.Random.Range(0f, Enumerable.Sum(Sectors, sector => sector.Probability));

        // Calculate the propability of each sector with respect to other sectors
        float cumulativeProbability = 0;
        // Random final sector accordingly to probability
        int randomFinalAngle = sectorsAngles[0];
        _finalSector = Sectors[0];

        for (int i = 0; i < Sectors.Length; i++)
        {
            cumulativeProbability += Sectors[i].Probability;

            if (rndNumber <= cumulativeProbability)
            {
                // Choose final sector
                randomFinalAngle = sectorsAngles[i];
                _finalSector = Sectors[i];
                break;
            }
        }

        int fullTurnovers = 5;

        // Set up how many turnovers our wheel should make before stop
        _finalAngle = fullTurnovers * 360 + randomFinalAngle - 20f;
        
        _isStarted = true;
    }
    
    private void Update()
    {
        if (!_isStarted)
            return;

        // Animation time
        float maxLerpRotationTime = 4f;

        // increment animation timer once per frame
        _currentLerpRotationTime += Time.deltaTime;

        // If the end of animation
        if (_currentLerpRotationTime > maxLerpRotationTime || Circle.transform.eulerAngles.z == _finalAngle)
        {
            _currentLerpRotationTime = maxLerpRotationTime;
            _isStarted = false;
            
            //if (_isFreeTurnAvailable)
                //EnableButton(FreeTurnButton);
            
            EnableButton(turnButton);
            EnableButton(btnClose);
            _startAngle = _finalAngle % 360;

            //GiveAwardByAngle ();
            _finalSector.RewardCallback.Invoke();
        }
        else
        {
            // Calculate current position using linear interpolation
            float t = _currentLerpRotationTime / maxLerpRotationTime;

            // This formulae allows to speed up at start and speed down at the end of rotation.
            // Try to change this values to customize the speed
            t = t * t * t * (t * (6f * t - 15f) + 10f);

            float angle = Mathf.Lerp(_startAngle, _finalAngle, t);
            Circle.transform.eulerAngles = new Vector3(0, 0, angle);
        }
    }


    public void RewardCoins(int awardCoins)
    {
        int moneyReward = awardCoins;
        
        if (_finalSector.typeRewardSpin == TypeRewardSpin.Pass)
        {
            moneyReward = 0;
            
            earnedObj.transform.localScale = Vector3.zero;
            earnedObj.SetActive(true);
            earnedObj.transform.DOScale(Vector3.one, .35f).SetEase(Ease.OutBack);
            earnedValueText.text = "ŞANSINI DAHA SONRA TEKRAR DENE!";
        }
        else if (_finalSector.typeRewardSpin == TypeRewardSpin.Gold)
        {
            moneyReward = awardCoins;
            
            Debug.Log("MONEY REWARD = " + moneyReward);
            
            earnedObj.transform.localScale = Vector3.zero;
            earnedObj.SetActive(true);
            earnedObj.transform.DOScale(Vector3.one, .35f).SetEase(Ease.OutBack);
            earnedValueText.text = $"+{moneyReward}";
            
            //ADD COINS
            //CurrencyController.Add(CurrencyType.Coins, moneyReward);
            //PlayfabManager.Instance.playfabCurrency.coins.Add(moneyReward);
            
            particle.Play();
        }
        
        timedButton.OnSpinFinished();
    }

    private void EnableButton(Button button)
    {
        button.interactable = true;
    }

    private void DisableButton(Button button)
    {
        button.interactable = false;
    }
    
}

/**
 * One sector on the wheel
 */
[Serializable]
public class FortuneWheelSector : System.Object
{
    public TypeRewardSpin typeRewardSpin;

    [Tooltip("Text object where value will be placed (not required)")]
    public GameObject ValueTextObject;

    [Tooltip("Value of reward")]
    public float RewardValue = 100;

    [Tooltip("Chance that this sector will be randomly selected")]
    [RangeAttribute(0, 100)]
    public float Probability = 100;

    [Tooltip("Method that will be invoked if this sector will be randomly selected")]
    public UnityEvent RewardCallback;
}
public enum TypeRewardSpin
{
    Pass,
    Gold
}