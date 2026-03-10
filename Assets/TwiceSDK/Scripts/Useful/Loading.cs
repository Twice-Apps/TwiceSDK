using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

public class Loading : MonoBehaviour
{
    public static Loading Instance { get; set; }
    
    public Slider slider_Loading;

    public float maxLoadingTime = 3f;
    public bool isBarLoaded = false;
    public bool isDataRetrieved = false;

    private bool isLoaded = false;
    public HeartbeatAnimation heartbeatAnimation;
    
    private void Awake()
    {
        Instance = this;
    }
    

    private void Start()
    {
        StartCoroutine(StartLoading());
    }

    private void Update()
    {
        if (isBarLoaded && isDataRetrieved && !isLoaded)
        {
            isLoaded = true;
            UnityEngine.SceneManagement.SceneManager.LoadScene(1);
        }
    }

    private IEnumerator StartLoading()
    {
        float finalValue = 100f;
        float currentTime = 0f;

        while (currentTime < 1)
        {
            currentTime += Time.deltaTime / maxLoadingTime;

            slider_Loading.value = Mathf.Lerp(currentTime, finalValue, currentTime);

            yield return null;
        }

        isBarLoaded = true;
        heartbeatAnimation.StopAnimation();
    }
}
