using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class Loader : MonoBehaviour
{
    public float loadTime;
    public Slider slider;
    
    private void Start()
    {
        slider.DOValue(100, loadTime).SetDelay(1).OnComplete(() =>
        {
            SceneManager.LoadScene(1);
        });
    }
}
