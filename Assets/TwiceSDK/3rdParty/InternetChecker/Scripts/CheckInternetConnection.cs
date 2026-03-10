using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class CheckInternetConnection : MonoBehaviour
{
    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    private bool isInternetAvailable = true;
    public GameObject Canvas;
    public GameObject TryAaginButton;
    public TextMeshProUGUI infoText;
    
    void Start()
    {
        Canvas.SetActive(false);
        // Check internet connection initially
        CheckInternetCon();

        // Invoke the CheckInternetConnection method every 5 or 10 seconds, still deciding
        InvokeRepeating(nameof(CheckInternetCon), 0f, 10f);
    }

    public void CheckInternetCon()
    {
        StartCoroutine(CheckInternetAvailabilityRoutine());
    }

    IEnumerator CheckInternetAvailabilityRoutine()
    {
        UnityWebRequest www = new UnityWebRequest("https://www.google.com");
        www.timeout = 5; // Set a timeout for the request
        infoText.text = "Baglaniyor...";
        TryAaginButton.SetActive(false);
        yield return www.SendWebRequest();

        if (www.result == UnityWebRequest.Result.ConnectionError || www.result == UnityWebRequest.Result.ProtocolError)
        {
            // Internet connection is not available
            isInternetAvailable = false;
            infoText.text = "Tekrar dene";
            Debug.LogError("Internet connection lost!");
        }
        else
        {
            // Internet connection is available
            isInternetAvailable = true;
            infoText.text = "Tekrar dene";
            Canvas.SetActive(false);
            Time.timeScale = 1f;
        }
    }

    void Update()
    {
        // Pause the game if internet connection is lost
        if (!isInternetAvailable)
        {
            Canvas.SetActive(true);
            TryAaginButton.SetActive(true);
            //infoText.text = "Please check your internet connection";
            Time.timeScale = 0f; // Pause the game
        }
    }
}
